using System.Security.Cryptography;

namespace DarkFactory.Core;

/// <summary>
/// Minimal ULID generator: a 48-bit millisecond timestamp followed by 80
/// bits of randomness, Crockford base32-encoded to 26 characters. Used for
/// spec_id (docs/adr/0016-spec-graph-content-addressed-append-only.md) —
/// lexicographically sortable by creation time, unlike a plain GUID.
/// </summary>
public static class Ulid
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewUlid() => NewUlid(DateTimeOffset.UtcNow);

    public static string NewUlid(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        var ms = (ulong)timestamp.ToUnixTimeMilliseconds();
        for (var i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(ms & 0xFF);
            ms >>= 8;
        }
        RandomNumberGenerator.Fill(bytes[6..]);

        Span<char> chars = stackalloc char[26];
        EncodeBase32(bytes, chars);
        return new string(chars);
    }

    private static void EncodeBase32(ReadOnlySpan<byte> data, Span<char> output)
    {
        // 128 bits -> 26 Crockford base32 characters (5 bits each, 130 bits
        // of capacity; the top 2 bits of the first character are always 0
        // since the timestamp component never needs them).
        ulong high = 0;
        for (var i = 0; i < 6; i++)
        {
            high = (high << 8) | data[i];
        }
        // high now holds the 48-bit timestamp in the low 48 bits.

        UInt128 value = ((UInt128)high << 80);
        for (var i = 6; i < 16; i++)
        {
            value |= (UInt128)data[i] << (8 * (15 - i));
        }

        for (var i = 25; i >= 0; i--)
        {
            output[i] = Crockford[(int)(value & 0x1F)];
            value >>= 5;
        }
    }
}
