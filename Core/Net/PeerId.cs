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
        /// <summary>Initializes a new instance of <see cref="PeerId"/> with the transport's name for the peer.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
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

        /// <summary>Peers are equal when their <see cref="Value"/> strings are equal, compared ordinally.</summary>
        public bool Equals(PeerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is PeerId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        /// <summary>Returns <see cref="Value"/>, or "(no peer)" for <see cref="None"/>.</summary>
        public override string ToString() => Value ?? "(no peer)";

        /// <summary>Returns true if both peers have the same <see cref="Value"/>.</summary>
        public static bool operator ==(PeerId left, PeerId right) => left.Equals(right);

        /// <summary>Returns true if the peers have different <see cref="Value"/>s.</summary>
        public static bool operator !=(PeerId left, PeerId right) => !left.Equals(right);
    }
}
