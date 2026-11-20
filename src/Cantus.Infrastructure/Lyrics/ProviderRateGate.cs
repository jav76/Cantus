using System.Net;
using System.Net.Http.Headers;
using Cantus.Infrastructure.Lyrics.Lookup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Shared outbound limits for the lyrics providers, consulted by
/// <see cref="ProviderRateGateHandler"/> before every request.
/// <list type="bullet">
/// <item>Lookup-tagged requests draw from a small per-provider budget per
/// rolling minute. Now-playing requests are never refused by a budget.</item>
/// <item>A throttle on now-playing traffic, or on any LRCLIB request, pauses
/// all traffic to that provider for the provider's Retry-After.</item>
/// <item>A throttle on a lookup-tagged NetEase request pauses lookups only, so
/// its unofficial search endpoint cannot take down the now-playing fallback.</item>
/// </list>
/// </summary>
public sealed class ProviderRateGate
{
    private const int DEFAULT_RETRY_AFTER_SECONDS = 60;
    private const int MIN_RETRY_AFTER_SECONDS = 1;
    private const int MAX_RETRY_AFTER_SECONDS = 300;

    private static readonly TimeSpan LookupBudgetWindow = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _timeProvider;
    private readonly IOptions<SongLookupOptions> _options;
    private readonly ILogger<ProviderRateGate> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, ProviderState> _states = new(StringComparer.Ordinal);

    public ProviderRateGate(
        TimeProvider timeProvider,
        IOptions<SongLookupOptions> options,
        ILogger<ProviderRateGate> logger)
    {
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Admits one request, spending a lookup permit when <paramref name="isLookup"/>
    /// is true. When refused, <paramref name="retryAfter"/> says how long until a
    /// request of the same kind would be admitted.
    /// </summary>
    public bool TryAcquire(string providerName, bool isLookup, out TimeSpan retryAfter)
    {
        ProviderPolicy policy = GetPolicy(providerName);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            ProviderState state = GetState(providerName);
            retryAfter = GetBlockedFor(state, policy, isLookup, now);
            if (retryAfter > TimeSpan.Zero)
            {
                return false;
            }

            if (isLookup)
            {
                state.LookupRequests.Enqueue(now);
            }

            return true;
        }
    }

    /// <summary>
    /// How long a request of this kind would be refused right now, or zero.
    /// Spends nothing.
    /// </summary>
    public TimeSpan GetRetryAfter(string providerName, bool isLookup)
    {
        ProviderPolicy policy = GetPolicy(providerName);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            return GetBlockedFor(GetState(providerName), policy, isLookup, now);
        }
    }

    /// <summary>
    /// Inspects a response the provider sent and starts a cooldown when it is a
    /// throttle. A 404 or any other status never does.
    /// </summary>
    /// <returns>True when a cooldown was started or extended.</returns>
    public bool ReportResponse(
        string providerName,
        bool isLookup,
        HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter)
    {
        ProviderPolicy policy = GetPolicy(providerName);
        bool coolsAllTraffic = !isLookup || policy.LookupThrottleCoolsAllTraffic;

        // A 503 without Retry-After is usually an outage, not a throttle. Only
        // the lookup-only cooldown is cheap enough to start on that guess.
        bool isThrottle = statusCode switch
        {
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.ServiceUnavailable => retryAfter is not null || !coolsAllTraffic,
            _ => false,
        };

        if (!isThrottle)
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan duration = ResolveRetryAfter(retryAfter, now);
        return StartCooldown(providerName, coolsAllTraffic, duration, now, (int)statusCode);
    }

    /// <summary>
    /// Pauses lookup traffic to a provider, for example after a payload
    /// rejection that arrived as HTTP 200. Now-playing traffic is unaffected.
    /// </summary>
    /// <returns>The effective cooldown after the default and clamp.</returns>
    public TimeSpan ReportLookupCooldown(string providerName, TimeSpan? retryAfter = null)
    {
        GetPolicy(providerName);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan duration = Clamp(retryAfter);
        StartCooldown(providerName, allTraffic: false, duration, now, statusCode: null);
        return duration;
    }

    private bool StartCooldown(
        string providerName,
        bool allTraffic,
        TimeSpan duration,
        DateTimeOffset now,
        int? statusCode)
    {
        DateTimeOffset until = now.Add(duration);
        bool extended;

        lock (_lock)
        {
            ProviderState state = GetState(providerName);
            if (allTraffic)
            {
                extended = until > state.AllTrafficUntil;
                if (extended)
                {
                    state.AllTrafficUntil = until;
                }
            }
            else
            {
                extended = until > state.LookupUntil;
                if (extended)
                {
                    state.LookupUntil = until;
                }
            }
        }

        if (extended)
        {
            _logger.LogWarning(
                "{Provider} throttled (status {StatusCode}); pausing {TrafficScope} requests until {CooldownUntilUtc}",
                providerName,
                statusCode,
                allTraffic ? "all" : "lookup",
                until);
        }

        return extended;
    }

    private static TimeSpan GetBlockedFor(
        ProviderState state,
        ProviderPolicy policy,
        bool isLookup,
        DateTimeOffset now)
    {
        DateTimeOffset blockedUntil = state.AllTrafficUntil;
        if (isLookup && state.LookupUntil > blockedUntil)
        {
            blockedUntil = state.LookupUntil;
        }

        if (blockedUntil > now)
        {
            return blockedUntil - now;
        }

        if (!isLookup)
        {
            return TimeSpan.Zero;
        }

        while (state.LookupRequests.Count > 0 && state.LookupRequests.Peek() + LookupBudgetWindow <= now)
        {
            state.LookupRequests.Dequeue();
        }

        return state.LookupRequests.Count >= policy.LookupRequestsPerMinute
            ? state.LookupRequests.Peek() + LookupBudgetWindow - now
            : TimeSpan.Zero;
    }

    private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        if (header?.Delta is TimeSpan delta)
        {
            return Clamp(delta);
        }

        if (header?.Date is DateTimeOffset date)
        {
            return Clamp(date - now);
        }

        return Clamp(null);
    }

    private static TimeSpan Clamp(TimeSpan? retryAfter)
    {
        if (retryAfter is null)
        {
            return TimeSpan.FromSeconds(DEFAULT_RETRY_AFTER_SECONDS);
        }

        TimeSpan min = TimeSpan.FromSeconds(MIN_RETRY_AFTER_SECONDS);
        TimeSpan max = TimeSpan.FromSeconds(MAX_RETRY_AFTER_SECONDS);
        if (retryAfter.Value < min)
        {
            return min;
        }

        return retryAfter.Value > max ? max : retryAfter.Value;
    }

    private ProviderPolicy GetPolicy(string providerName)
    {
        SongLookupOptions options = _options.Value;
        return providerName switch
        {
            // LRCLIB's terms ask clients to honour its throttles on all traffic.
            LrclibLyricsProvider.PROVIDER_NAME => new(Math.Max(1, options.LrclibRequestsPerMinute), true),
            NeteaseLyricsProvider.PROVIDER_NAME => new(Math.Max(1, options.NeteaseRequestsPerMinute), false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(providerName),
                providerName,
                "No rate policy is defined for this lyrics provider."),
        };
    }

    private ProviderState GetState(string providerName)
    {
        if (!_states.TryGetValue(providerName, out ProviderState? state))
        {
            state = new();
            _states[providerName] = state;
        }

        return state;
    }

    private readonly record struct ProviderPolicy(int LookupRequestsPerMinute, bool LookupThrottleCoolsAllTraffic);

    private sealed class ProviderState
    {
        public DateTimeOffset AllTrafficUntil { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset LookupUntil { get; set; } = DateTimeOffset.MinValue;

        public Queue<DateTimeOffset> LookupRequests { get; } = new();
    }
}
