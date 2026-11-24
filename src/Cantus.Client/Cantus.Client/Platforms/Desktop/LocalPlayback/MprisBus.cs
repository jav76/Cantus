using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Every D-Bus call the MPRIS source makes, against Tmds.DBus.Protocol 0.21.3 (the version Uno's X11
/// host ships). Values leave this class as plain .NET types so nothing else depends on the library.
/// Calls throw <see cref="DBusException"/> or <see cref="DisconnectedException"/> when a player
/// vanishes mid-call, and <see cref="TimeoutException"/> when it does not answer within
/// <see cref="CALL_TIMEOUT"/> (or the longer <see cref="SETUP_CALL_TIMEOUT"/> for calls that find and
/// first read players).
/// Player calls take the player's unique connection name as <c>destination</c> and never auto-start
/// a service, so a call racing a player's exit cannot D-Bus-activate it again.
/// </summary>
internal sealed class MprisBus : IDisposable
{
    public const string ROOT_INTERFACE = "org.mpris.MediaPlayer2";
    public const string PLAYER_INTERFACE = "org.mpris.MediaPlayer2.Player";
    public const string OBJECT_PATH = "/org/mpris/MediaPlayer2";

    private const string PROPERTIES_INTERFACE = "org.freedesktop.DBus.Properties";
    private const string BUS_SERVICE = "org.freedesktop.DBus";
    private const string BUS_PATH = "/org/freedesktop/DBus";

    // The session bus has no reply timeout, so a player whose main loop is frozen would never answer.
    public static readonly TimeSpan CALL_TIMEOUT = TimeSpan.FromSeconds(2);

    // libdbus and GDBus's default: a player busy starting up still gets found.
    public static readonly TimeSpan SETUP_CALL_TIMEOUT = TimeSpan.FromSeconds(25);

    private Connection? _connection;

    public static bool HasSessionBusAddress => !string.IsNullOrWhiteSpace(Address.Session);

    /// <summary>True when a call failed because the player's bus name no longer has an owner.</summary>
    public static bool IsNameGone(Exception exception) => exception is DBusException
    {
        ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner",
    };

    public async Task ConnectAsync()
    {
        string? address = Address.Session;
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("No D-Bus session bus address is set.");
        }

        Connection connection = new(address);
        try
        {
            await connection.ConnectAsync();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        _connection = connection;
    }

    /// <summary>Completes when the bus connection drops.</summary>
    public Task<Exception?> WhenDisconnectedAsync() => RequireConnection().DisconnectedAsync();

    public Task<string[]> ListNamesAsync()
    {
        Connection connection = RequireConnection();
        return WithTimeout(
            connection.CallMethodAsync(
                CreateBusCall(connection, "ListNames", argument: null),
                static (Message message, object? _) => message.GetBodyReader().ReadArrayOfString(),
                null),
            SETUP_CALL_TIMEOUT);
    }

    public Task<string> GetNameOwnerAsync(string name)
    {
        Connection connection = RequireConnection();
        return WithTimeout(
            connection.CallMethodAsync(
                CreateBusCall(connection, "GetNameOwner", name),
                static (Message message, object? _) => message.GetBodyReader().ReadString(),
                null),
            SETUP_CALL_TIMEOUT);
    }

    public Task<Dictionary<string, object?>> GetAllPropertiesAsync(
        string destination,
        string interfaceName,
        TimeSpan? timeout = null)
    {
        Connection connection = RequireConnection();
        return WithTimeout(
            connection.CallMethodAsync(
                CreatePropertiesCall(connection, destination, "GetAll", interfaceName, propertyName: null),
                static (Message message, object? _) => ToDictionary(message.GetBodyReader().ReadDictionaryOfStringToVariantValue()),
                null),
            timeout ?? CALL_TIMEOUT);
    }

    public Task<object?> GetPlayerPropertyAsync(string destination, string propertyName)
    {
        Connection connection = RequireConnection();
        return WithTimeout(connection.CallMethodAsync(
            CreatePropertiesCall(connection, destination, "Get", PLAYER_INTERFACE, propertyName),
            static (Message message, object? _) => ToClr(message.GetBodyReader().ReadVariantValue()),
            null));
    }

    /// <summary>Calls an argument-less player method such as <c>PlayPause</c> or <c>Next</c>.</summary>
    public Task CallPlayerMethodAsync(string destination, string member)
    {
        Connection connection = RequireConnection();
        MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, OBJECT_PATH, PLAYER_INTERFACE, member, null, MessageFlags.NoAutoStart);
        return WithTimeout(connection.CallMethodAsync(TakeMessage(ref writer)));
    }

    public Task SetPositionAsync(string destination, string trackObjectPath, long positionMicroseconds)
    {
        Connection connection = RequireConnection();
        MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, OBJECT_PATH, PLAYER_INTERFACE, "SetPosition", "ox", MessageFlags.NoAutoStart);
        writer.WriteObjectPath(trackObjectPath);
        writer.WriteInt64(positionMicroseconds);
        return WithTimeout(connection.CallMethodAsync(TakeMessage(ref writer)));
    }

    public async Task<IDisposable> WatchNameOwnerChangedAsync(Action<string, string, string> handler)
    {
        MatchRule rule = new()
        {
            Type = MessageType.Signal,
            Sender = BUS_SERVICE,
            Path = BUS_PATH,
            Interface = BUS_SERVICE,
            Member = "NameOwnerChanged",
        };

        return await RequireConnection().AddMatchAsync(
            rule,
            static (Message message, object? _) =>
            {
                Reader reader = message.GetBodyReader();
                return (Name: reader.ReadString(), OldOwner: reader.ReadString(), NewOwner: reader.ReadString());
            },
            (Exception? ex, (string Name, string OldOwner, string NewOwner) change, object? _, object? _) =>
            {
                if (ex is null)
                {
                    handler(change.Name, change.OldOwner, change.NewOwner);
                }
            },
            ObserverFlags.None,
            null,
            null,
            false);
    }

    /// <summary>Every <c>PropertiesChanged</c> on the MPRIS object path: (sender, interface, changed, invalidated).</summary>
    public async Task<IDisposable> WatchPropertiesChangedAsync(
        Action<string, string, Dictionary<string, object?>, string[]> handler)
    {
        MatchRule rule = new()
        {
            Type = MessageType.Signal,
            Path = OBJECT_PATH,
            Interface = PROPERTIES_INTERFACE,
            Member = "PropertiesChanged",
        };

        return await RequireConnection().AddMatchAsync(
            rule,
            static (Message message, object? _) =>
            {
                Reader reader = message.GetBodyReader();
                string interfaceName = reader.ReadString();
                Dictionary<string, object?> changed = ToDictionary(reader.ReadDictionaryOfStringToVariantValue());
                string[] invalidated = reader.ReadArrayOfString();
                return (Sender: message.SenderAsString ?? string.Empty, Interface: interfaceName, Changed: changed, Invalidated: invalidated);
            },
            (Exception? ex, (string Sender, string Interface, Dictionary<string, object?> Changed, string[] Invalidated) signal, object? _, object? _) =>
            {
                if (ex is null)
                {
                    handler(signal.Sender, signal.Interface, signal.Changed, signal.Invalidated);
                }
            },
            ObserverFlags.None,
            null,
            null,
            false);
    }

    /// <summary>Every MPRIS <c>Seeked</c> signal: (sender, position in microseconds).</summary>
    public async Task<IDisposable> WatchSeekedAsync(Action<string, long> handler)
    {
        MatchRule rule = new()
        {
            Type = MessageType.Signal,
            Path = OBJECT_PATH,
            Interface = PLAYER_INTERFACE,
            Member = "Seeked",
        };

        return await RequireConnection().AddMatchAsync(
            rule,
            static (Message message, object? _) =>
                (Sender: message.SenderAsString ?? string.Empty, Position: message.GetBodyReader().ReadInt64()),
            (Exception? ex, (string Sender, long Position) signal, object? _, object? _) =>
            {
                if (ex is null)
                {
                    handler(signal.Sender, signal.Position);
                }
            },
            ObserverFlags.None,
            null,
            null,
            false);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private static async Task<T> WithTimeout<T>(Task<T> call, TimeSpan? timeout = null)
    {
        try
        {
            return await call.WaitAsync(timeout ?? CALL_TIMEOUT).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ObserveAbandoned(call);
            throw;
        }
    }

    private static async Task WithTimeout(Task call)
    {
        try
        {
            await call.WaitAsync(CALL_TIMEOUT).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ObserveAbandoned(call);
            throw;
        }
    }

    // The abandoned call completes when the reply finally arrives or the connection closes.
    private static void ObserveAbandoned(Task call) =>
        _ = call.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private Connection RequireConnection() =>
        _connection ?? throw new InvalidOperationException("The session bus is not connected.");

    private static MessageBuffer CreateBusCall(Connection connection, string member, string? argument)
    {
        MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            BUS_SERVICE,
            BUS_PATH,
            BUS_SERVICE,
            member,
            argument is null ? null : "s",
            MessageFlags.None);
        if (argument is not null)
        {
            writer.WriteString(argument);
        }

        return TakeMessage(ref writer);
    }

    private static MessageBuffer CreatePropertiesCall(
        Connection connection,
        string destination,
        string member,
        string interfaceName,
        string? propertyName)
    {
        MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination,
            OBJECT_PATH,
            PROPERTIES_INTERFACE,
            member,
            propertyName is null ? "s" : "ss",
            MessageFlags.NoAutoStart);
        writer.WriteString(interfaceName);
        if (propertyName is not null)
        {
            writer.WriteString(propertyName);
        }

        return TakeMessage(ref writer);
    }

    private static MessageBuffer TakeMessage(ref MessageWriter writer)
    {
        try
        {
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static Dictionary<string, object?> ToDictionary(Dictionary<string, VariantValue> values)
    {
        Dictionary<string, object?> result = new(values.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, VariantValue> pair in values)
        {
            result[pair.Key] = ToClr(pair.Value);
        }

        return result;
    }

    private static object? ToClr(VariantValue value)
    {
        try
        {
            return value.Type switch
            {
                VariantValueType.String => value.GetString(),
                VariantValueType.ObjectPath => value.GetObjectPathAsString(),
                VariantValueType.Signature => value.GetSignature().ToString(),
                VariantValueType.Bool => value.GetBool(),
                VariantValueType.Byte => value.GetByte(),
                VariantValueType.Int16 => value.GetInt16(),
                VariantValueType.UInt16 => value.GetUInt16(),
                VariantValueType.Int32 => value.GetInt32(),
                VariantValueType.UInt32 => value.GetUInt32(),
                VariantValueType.Int64 => value.GetInt64(),
                VariantValueType.UInt64 => value.GetUInt64(),
                VariantValueType.Double => value.GetDouble(),
                VariantValueType.Variant => ToClr(value.GetVariantValue()),
                VariantValueType.Array => ToArray(value),
                VariantValueType.Dictionary => ToDictionary(value),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static object? ToArray(VariantValue value)
    {
        if (value.ItemType is VariantValueType.String or VariantValueType.ObjectPath)
        {
            string[] strings = new string[value.Count];
            for (int i = 0; i < strings.Length; i++)
            {
                strings[i] = ToClr(value.GetItem(i)) as string ?? string.Empty;
            }

            return strings;
        }

        object?[] items = new object?[value.Count];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = ToClr(value.GetItem(i));
        }

        return items;
    }

    private static Dictionary<string, object?> ToDictionary(VariantValue value)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        for (int i = 0; i < value.Count; i++)
        {
            KeyValuePair<VariantValue, VariantValue> entry = value.GetDictionaryEntry(i);
            if (ToClr(entry.Key) is string key)
            {
                result[key] = ToClr(entry.Value);
            }
        }

        return result;
    }
}
