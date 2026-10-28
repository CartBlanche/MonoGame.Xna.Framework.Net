using System;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Names a remote machine to a transport, whatever the transport is: an IP address and port for
    /// UDP, a SteamID for Steam, a product user id for Epic. The framework never looks inside it; only
    /// the transport that issued it can say where it leads.
    /// </summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public PeerId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The transport's own name for the peer. Opaque to everything else.</summary>
        public string Value { get; }

        /// <summary>False for <see cref="None"/>.</summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);

        /// <summary>No peer.</summary>
        public static PeerId None => default;

        public bool Equals(PeerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PeerId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? "(no peer)";

        public static bool operator ==(PeerId left, PeerId right) => left.Equals(right);

        public static bool operator !=(PeerId left, PeerId right) => !left.Equals(right);
    }
}
