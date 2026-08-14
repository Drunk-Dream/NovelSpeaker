using System.Runtime.InteropServices;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryDpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public static void TryEnableFixedDpi()
    {
        try
        {
            _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch (DllNotFoundException)
        {
            // The renderer still writes 96 DPI PNG metadata on non-Windows hosts.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions keep the WPF process default.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
