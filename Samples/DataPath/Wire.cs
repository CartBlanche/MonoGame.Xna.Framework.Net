using Microsoft.Xna.Framework;

namespace DataPath;

/// <summary>The first byte of every packet this sample sends.</summary>
internal enum Kind : byte
{
    Start = 1,      // host to everyone, reliable: begin, and for how long
    Position,       // unreliable: where the sender is
    Sequenced,      // in order, not reliable: a counter that must never go backwards
    Event,          // reliable and in order: numbered, must arrive once, in order, with nothing missing
    Blob,           // reliable and in order: one large message that gets split and put back together
    Done            // reliable and in order, last: what the sender sent, so the receiver can check it all arrived
}

internal static class Wire
{
    public const int BlobSize = 60_000;

    /// <summary>Where a sender is at a sequence number. Receivers recompute it, so a corrupted value is noticed.</summary>
    public static Vector2 PositionAt(uint sequence) => new Vector2(sequence * 0.25f, MathF.Sin(sequence * 0.1f) * 50f);

    /// <summary>The same bytes for the same seed on every machine.</summary>
    public static byte[] MakeBlob(uint seed)
    {
        var bytes = new byte[BlobSize];
        uint x = seed * 2654435761u + 1;
        for (int i = 0; i < bytes.Length; i++)
        {
            x = x * 1664525u + 1013904223u;
            bytes[i] = (byte)(x >> 24);
        }
        return bytes;
    }

    public static uint Checksum(byte[] data)
    {
        uint hash = 2166136261;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }
}
