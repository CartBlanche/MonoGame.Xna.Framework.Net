using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// What a session password turns into: a key, a proof that the joiner knows it, and a wrapping of the
    /// link key that only somebody who knows it can open. The password itself never crosses the network.
    /// </summary>
    internal static class JoinCredentials
    {
        // Deliberately modest: a joiner pays this once, a host once per password, and a phone must not stall.
        // It makes guessing a LAN party's password from a captured handshake slower, not impossible.
        private const int KeyDerivationIterations = 20_000;

        public const int SaltLength = 16;
        public const int NonceLength = 16;
        public const int LinkKeyLength = 32;

        public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

        public static byte[] DeriveKey(string password, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, KeyDerivationIterations, HashAlgorithmName.SHA256, 32);

        /// <summary>Shows the host the joiner knows the password, for this gamer and this challenge only.</summary>
        public static byte[] Proof(byte[] passwordKey, string gamerId, byte[] challengeNonce) =>
            HMACSHA256.HashData(passwordKey, Concat("mgnet-join", Encoding.UTF8.GetBytes(gamerId), challengeNonce));

        /// <summary>The key that seals the link key in the acceptance: tied to this challenge, so it is never reused.</summary>
        public static byte[] WrapKey(byte[] passwordKey, byte[] challengeNonce) =>
            HMACSHA256.HashData(passwordKey, Concat("mgnet-wrap", challengeNonce));

        public static bool ProofMatches(byte[] expected, byte[] given) =>
            given != null && CryptographicOperations.FixedTimeEquals(expected, given);

        /// <summary>Encrypts and authenticates: random nonce, then ciphertext, then tag.</summary>
        public static byte[] Seal(byte[] key, byte[] plaintext, byte[] associatedData)
        {
            var nonce = RandomBytes(12);
            var sealedBytes = new byte[12 + plaintext.Length + 16];
            Buffer.BlockCopy(nonce, 0, sealedBytes, 0, 12);
            using (var aes = new AesGcm(key, 16))
                aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(12, plaintext.Length), sealedBytes.AsSpan(12 + plaintext.Length, 16), associatedData);
            return sealedBytes;
        }

        /// <summary>Reverses <see cref="Seal"/>; false if the key is wrong or anything was changed.</summary>
        public static bool TryOpen(byte[] key, byte[] sealedBytes, byte[] associatedData, out byte[] plaintext)
        {
            plaintext = null;
            if (sealedBytes == null || sealedBytes.Length < 12 + 16)
                return false;

            int length = sealedBytes.Length - 12 - 16;
            var result = new byte[length];
            try
            {
                using (var aes = new AesGcm(key, 16))
                    aes.Decrypt(sealedBytes.AsSpan(0, 12), sealedBytes.AsSpan(12, length), sealedBytes.AsSpan(12 + length, 16), result, associatedData);
            }
            catch (CryptographicException)
            {
                return false;
            }

            plaintext = result;
            return true;
        }

        private static byte[] Concat(string label, params byte[][] parts)
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            int total = labelBytes.Length + 1;
            foreach (var part in parts)
                total += part.Length + 4;
            var result = new byte[total];
            int at = 0;
            Buffer.BlockCopy(labelBytes, 0, result, at, labelBytes.Length);
            at += labelBytes.Length + 1;
            foreach (var part in parts)
            {
                // length-prefixed, so "ab"+"c" and "a"+"bc" are different inputs
                BitConverter.TryWriteBytes(result.AsSpan(at, 4), part.Length);
                at += 4;
                Buffer.BlockCopy(part, 0, result, at, part.Length);
                at += part.Length;
            }
            return result;
        }
    }
}
