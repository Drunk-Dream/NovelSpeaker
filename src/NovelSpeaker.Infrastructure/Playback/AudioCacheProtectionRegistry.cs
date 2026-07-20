using System.Collections.Concurrent;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Tracks cache paths that are currently in use by playback or cache writes.
/// </summary>
public sealed class AudioCacheProtectionRegistry : IAudioCacheProtectionRegistry
{
    private readonly ConcurrentDictionary<string, int> _protectedPaths = new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Protect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        _protectedPaths.AddOrUpdate(normalizedPath, 1, static (_, count) => count + 1);
        return new Releaser(this, normalizedPath);
    }

    public bool IsProtected(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _protectedPaths.ContainsKey(Path.GetFullPath(filePath));
    }

    private void Release(string normalizedPath)
    {
        while (true)
        {
            if (!_protectedPaths.TryGetValue(normalizedPath, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                if (_protectedPaths.TryRemove(normalizedPath, out _))
                {
                    return;
                }

                continue;
            }

            if (_protectedPaths.TryUpdate(normalizedPath, count - 1, count))
            {
                return;
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly AudioCacheProtectionRegistry _registry;
        private readonly string _normalizedPath;
        private bool _disposed;

        public Releaser(AudioCacheProtectionRegistry registry, string normalizedPath)
        {
            _registry = registry;
            _normalizedPath = normalizedPath;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registry.Release(_normalizedPath);
        }
    }
}
