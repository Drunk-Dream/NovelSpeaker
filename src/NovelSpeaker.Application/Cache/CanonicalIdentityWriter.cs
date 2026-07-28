using System.Text;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Writes length-delimited fields so identity serialization cannot be ambiguous.
/// </summary>
internal sealed class CanonicalIdentityWriter
{
    private readonly StringBuilder _builder = new();

    public void Add(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        value ??= string.Empty;
        _builder
            .Append(name)
            .Append('=')
            .Append(value.Length)
            .Append(':')
            .Append(value)
            .Append('\n');
    }

    public Fingerprint Build() => Fingerprint.Sha256(_builder.ToString());
}
