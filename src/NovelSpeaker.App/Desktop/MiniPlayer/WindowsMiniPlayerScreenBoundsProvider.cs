using System.Runtime.InteropServices;

namespace NovelSpeaker.App.Desktop.MiniPlayer;

internal sealed class WindowsMiniPlayerScreenBoundsProvider : IMiniPlayerScreenBoundsProvider
{
    public IReadOnlyList<MiniPlayerScreenBounds> GetWorkAreas()
    {
        var bounds = new List<MiniPlayerScreenBounds>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    bounds.Add(new MiniPlayerScreenBounds(
                        info.WorkArea.Left,
                        info.WorkArea.Top,
                        info.WorkArea.Right - info.WorkArea.Left,
                        info.WorkArea.Bottom - info.WorkArea.Top));
                }

                return true;
            },
            IntPtr.Zero);
        return bounds;
    }

    private delegate bool MonitorEnumProcedure(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRectangle,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clippingRectangle,
        MonitorEnumProcedure callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
