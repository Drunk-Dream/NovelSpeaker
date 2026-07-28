using System.Security.Cryptography;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Immutable binary SHA-256 fingerprint suitable for SQLite BLOB storage.
/// </summary>
public sealed class Fingerprint : IEquatable<Fingerprint>
{
    private readonly byte[] _bytes;

    public Fingerprint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Fingerprint cannot be empty.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string Hex => Convert.ToHexString(_bytes).ToLowerInvariant();

    public static Fingerprint Sha256(ReadOnlySpan<byte> value) =>
        new(SHA256.HashData(value));

    public static Fingerprint Sha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256(System.Text.Encoding.UTF8.GetBytes(value));
    }

    public bool Equals(Fingerprint? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as Fingerprint);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _bytes)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Hex;

    public byte[] ToArray() => _bytes.ToArray();
}
