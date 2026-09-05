using System;

namespace Cantus.Client.Services;

public static class WasmInterop
{
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
            Console.WriteLine($"[WasmInterop] GetCurrentOrigin failed: {ex.Message}");
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
            Console.WriteLine($"[WasmInterop] NavigateTo failed: {ex.Message}");
        }
#endif
    }

    public static void CleanAuthQuery()
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.CantusInterop && window.CantusInterop.cleanAuthQuery && window.CantusInterop.cleanAuthQuery()");
        }
        catch
        {
        }
#endif
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
        catch
        {
            return true;
        }
#else
        return true;
#endif
    }
}
