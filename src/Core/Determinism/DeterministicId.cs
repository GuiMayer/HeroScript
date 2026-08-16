using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Core.Determinism;

/// <summary>
/// Generates UUIDv8 identifiers from run-owned deterministic inputs.
/// </summary>
public static class DeterministicId
{
    public static Guid Create(ulong seed, ulong sequence, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var scopeBytes = Encoding.UTF8.GetBytes(scope);
        var input = new byte[sizeof(ulong) * 2 + scopeBytes.Length];
        BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(0, sizeof(ulong)), seed);
        BinaryPrimitives.WriteUInt64BigEndian(input.AsSpan(sizeof(ulong), sizeof(ulong)), sequence);
        scopeBytes.CopyTo(input.AsSpan(sizeof(ulong) * 2));

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        Span<byte> uuidBytes = hash[..16];

        // RFC 9562 variant and application-defined UUID version 8.
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x80);
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

        var hex = Convert.ToHexString(uuidBytes);
        return Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}",
            "D");
    }
}
