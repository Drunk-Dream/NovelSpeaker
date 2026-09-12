using Microsoft.Extensions.Logging;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Bridges the synchronous ILoggerProvider lifetime to the concrete provider's async owner.
/// </summary>
internal sealed class LoggerProviderRegistrationAdapter : ILoggerProvider
{
    private readonly RollingFileLoggerProvider _provider;

    public LoggerProviderRegistrationAdapter(RollingFileLoggerProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ILogger CreateLogger(string categoryName) => _provider.CreateLogger(categoryName);

    public void Dispose()
    {
        // The concrete provider is disposed asynchronously by the owning service provider.
    }
}
