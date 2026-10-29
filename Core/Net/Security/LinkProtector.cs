using System;
using System.Security.Cryptography;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Protects every datagram on one host-to-client link with the key the host issued that client:
    /// nobody who lacks the key can forge a datagram on the link (it fails authentication and is dropped),
    /// and, when the key was handed over under a session password, nobody can read it either.
    /// </summary>
    /// <remarks>
    /// Frame: <c>[FrameType][nonce 12][ciphertext][tag 16]</c>, 29 bytes of overhead. The direction (host to
    /// client or back) is authenticated, so a datagram cannot be reflected at its own sender. Without a
    /// password the key travels in the clear, so this keeps off-path forgers out but not somebody who can
    /// read the LAN. Replayed datagrams are not rejected here (the channel discards duplicates).
    /// </remarks>
    internal sealed class LinkProtector : IDisposable
    {
        /// <summary>The first byte of a protected datagram.</summary>
        public const byte FrameType = 0x27;

        public const int Overhead = 1 + 12 + 16;

        private readonly AesGcm aes;
        private readonly object gate = new object();
        private readonly byte sendDirection;
        private readonly byte receiveDirection;

        public LinkProtector(byte[] key, bool weAreTheHost)
        {
            aes = new AesGcm(key, 16);
            sendDirection = (byte)(weAreTheHost ? 0 : 1);
            receiveDirection = (byte)(weAreTheHost ? 1 : 0);
        }

        public byte[] Protect(byte[] plain, int length)
        {
            var frame = new byte[Overhead + length];
            frame[0] = FrameType;
            RandomNumberGenerator.Fill(frame.AsSpan(1, 12));
            lock (gate)
                aes.Encrypt(frame.AsSpan(1, 12), plain.AsSpan(0, length), frame.AsSpan(13, length), frame.AsSpan(13 + length, 16), new[] { sendDirection });
            return frame;
        }

        public bool TryUnprotect(byte[] frame, int length, out byte[] plain)
        {
            plain = null;
            if (length < Overhead || frame[0] != FrameType)
                return false;

            int bodyLength = length - Overhead;
            var result = new byte[bodyLength];
            try
            {
                lock (gate)
                    aes.Decrypt(frame.AsSpan(1, 12), frame.AsSpan(13, bodyLength), frame.AsSpan(13 + bodyLength, 16), result, new[] { receiveDirection });
            }
            catch (CryptographicException)
            {
                return false;
            }

            plain = result;
            return true;
        }

        public void Dispose() => aes.Dispose();
    }
}
