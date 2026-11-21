using System;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public static class WasmInterop
{
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger(typeof(WasmInterop).FullName ?? nameof(WasmInterop));

    public static string GetCurrentOrigin()
    {
#if __WASM__
        try
        {
            string origin = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop ? window.CantusInterop.getOrigin() : window.location.origin");
            if (!string.IsNullOrWhiteSpace(origin) && origin != "null" && origin != "undefined")
            {
                return origin.Trim().TrimEnd('/');
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentOrigin failed");
        }
#endif
        return string.Empty;
    }

    public static void NavigateTo(string url)
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                $"window.CantusInterop ? window.CantusInterop.navigateTo('{url}') : (window.location.href = '{url}')");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateTo failed");
        }
#endif
    }

    public static string GetAuthQueryParameter()
    {
#if __WASM__
        try
        {
            string token = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.getAuthQuery ? window.CantusInterop.getAuthQuery() : (new URLSearchParams(window.location.search).get('auth') || '')");
            if (!string.IsNullOrWhiteSpace(token) && token != "null" && token != "undefined")
            {
                return token.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAuthQueryParameter failed");
        }
#endif
        return string.Empty;
    }

    public static void CleanAuthQuery()
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.cleanAuthQuery && window.CantusInterop.cleanAuthQuery()");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CleanAuthQuery failed");
        }
#endif
    }

    /// <summary>The page address's query string, including the leading <c>?</c>.</summary>
    public static string GetLocationSearch()
    {
#if __WASM__
        try
        {
            string search = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.getLocationSearch ? window.CantusInterop.getLocationSearch() : (window.location.search || '')");
            if (!string.IsNullOrWhiteSpace(search) && search != "null" && search != "undefined")
            {
                return search.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetLocationSearch failed");
        }
#endif
        return string.Empty;
    }

    /// <summary>Drops one query parameter from the address bar without reloading the page.</summary>
    /// <param name="name">A parameter name made of letters only.</param>
    public static void RemoveQueryParameter(string name)
    {
#if __WASM__
        if (string.IsNullOrEmpty(name) || !IsAsciiLetters(name))
        {
            return;
        }

        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                $"window.CantusInterop && window.CantusInterop.removeQueryParameter && window.CantusInterop.removeQueryParameter('{name}')");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RemoveQueryParameter failed");
        }
#endif
    }

    private static bool IsAsciiLetters(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsDocumentVisible()
    {
#if __WASM__
        try
        {
            string val = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.isDocumentVisible ? (window.CantusInterop.isDocumentVisible() ? 'true' : 'false') : 'true'");
            return bool.TryParse(val, out bool isVis) ? isVis : true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IsDocumentVisible check failed");
            return true;
        }
#else
        return true;
#endif
    }
}
