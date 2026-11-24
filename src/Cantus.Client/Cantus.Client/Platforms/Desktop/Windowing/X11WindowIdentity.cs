using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cantus.Client.Models;

namespace Cantus.Client.Windowing;

/// <summary>
/// Sets WM_CLASS and _NET_WM_ICON on the app's X11 window. Uno's X11 host leaves WM_CLASS unset and
/// only reads an icon from a package manifest, so without these a desktop environment shows a
/// generic icon and cannot match the window to cantus.desktop (its <c>StartupWMClass</c>).
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11WindowIdentity
{
    public const string INSTANCE_NAME = "cantus";
    public const string CLASS_NAME = "Cantus";

    private const string LIB_X11 = "libX11.so.6";
    private const string NET_WM_ICON = "_NET_WM_ICON";
    private const nint XA_CARDINAL = 6;
    private const int FORMAT_32_BIT = 32;
    private const int PROP_MODE_REPLACE = 0;

    public static void Apply(nint windowId, string iconPath)
    {
        // A property can be set from any connection, so this does not need Uno's own display handle.
        nint display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SetClass(display, windowId);
            if (File.Exists(iconPath))
            {
                SetIcon(display, windowId, WindowIconProperty.Build(File.ReadAllBytes(iconPath)));
            }

            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static void SetClass(nint display, nint windowId)
    {
        XClassHint hint = new()
        {
            ResName = Marshal.StringToHGlobalAnsi(INSTANCE_NAME),
            ResClass = Marshal.StringToHGlobalAnsi(CLASS_NAME)
        };
        try
        {
            XSetClassHint(display, windowId, ref hint);
        }
        finally
        {
            Marshal.FreeHGlobal(hint.ResName);
            Marshal.FreeHGlobal(hint.ResClass);
        }
    }

    private static void SetIcon(nint display, nint windowId, nint[] property)
    {
        nint atom = XInternAtom(display, NET_WM_ICON, false);
        XChangeProperty(display, windowId, atom, XA_CARDINAL, FORMAT_32_BIT, PROP_MODE_REPLACE, property, property.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public nint ResName;
        public nint ResClass;
    }

    [DllImport(LIB_X11)]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport(LIB_X11)]
    private static extern int XSetClassHint(nint display, nint window, ref XClassHint classHint);

    [DllImport(LIB_X11, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern nint XInternAtom(nint display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(LIB_X11)]
    private static extern int XChangeProperty(
        nint display,
        nint window,
        nint property,
        nint type,
        int format,
        int mode,
        nint[] data,
        int elementCount);

    [DllImport(LIB_X11)]
    private static extern int XFlush(nint display);

    [DllImport(LIB_X11)]
    private static extern int XCloseDisplay(nint display);
}
