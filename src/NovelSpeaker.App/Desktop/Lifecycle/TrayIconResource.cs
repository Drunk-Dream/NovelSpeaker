namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class TrayIconResource : IDisposable
{
    private readonly Func<IntPtr, bool>? _destroy;
    private IntPtr _handle;

    private TrayIconResource(IntPtr handle, Func<IntPtr, bool>? destroy)
    {
        _handle = handle != IntPtr.Zero
            ? handle
            : throw new ArgumentException("A tray icon handle is required.", nameof(handle));
        _destroy = destroy;
    }

    public IntPtr Handle => Volatile.Read(ref _handle);

    public static TrayIconResource Owned(IntPtr handle, Func<IntPtr, bool> destroy)
    {
        ArgumentNullException.ThrowIfNull(destroy);
        return new TrayIconResource(handle, destroy);
    }

    public static TrayIconResource Shared(IntPtr handle)
    {
        return new TrayIconResource(handle, null);
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            _destroy?.Invoke(handle);
        }
    }
}
