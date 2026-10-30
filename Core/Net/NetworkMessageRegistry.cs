using System;
using System.Collections.Concurrent;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Provides a registry for network message types, allowing messages to be registered and created dynamically.
    /// </summary>
    public static class NetworkMessageRegistry
    {
        private struct Entry
        {
            public Type Type;
            public Func<INetworkMessage> Create;
        }

        /// <summary>
        /// The first message id open to games and third-party backends. Ids below it belong to the framework,
        /// and ids from here up to 255 are yours; the framework will never use them.
        /// </summary>
        public const byte FirstCustomId = 128;

        private static readonly ConcurrentDictionary<byte, Entry> registry = new();

        /// <summary>
        /// Registers your own network message type with the specified type identifier, from
        /// <see cref="FirstCustomId"/> (128) to 255. Send it with <see cref="NetworkSession.SendMessage(INetworkMessage, SendDataOptions)"/>
        /// and receive it through <see cref="NetworkSession.MessageReceived"/>.
        /// Registering the same type under the same identifier again is harmless.
        /// </summary>
        /// <typeparam name="T">The type of the network message to register. Must implement <see cref="INetworkMessage"/> and have a parameterless constructor.</typeparam>
        /// <param name="typeId">The unique identifier for the message type.</param>
        /// <exception cref="ArgumentOutOfRangeException">The identifier is below <see cref="FirstCustomId"/>: those belong to the framework.</exception>
        /// <exception cref="InvalidOperationException">The identifier already belongs to a different type.</exception>
        public static void Register<T>(byte typeId) where T : INetworkMessage, new()
        {
            if (typeId < FirstCustomId)
                throw new ArgumentOutOfRangeException(nameof(typeId), typeId,
                    $"Message ids below {FirstCustomId} are the framework's own; use {FirstCustomId} to 255.");
            RegisterCore<T>(typeId);
        }

        private static void RegisterCore<T>(byte typeId) where T : INetworkMessage, new()
        {
            var entry = new Entry { Type = typeof(T), Create = () => new T() };
            var existing = registry.GetOrAdd(typeId, entry);
            if (existing.Type != typeof(T))
                throw new InvalidOperationException(
                    $"Message id {typeId} is already registered to {existing.Type.Name}; {typeof(T).Name} cannot use it.");
        }

        /// <summary>
        /// Creates a network message instance based on the specified type identifier.
        /// </summary>
        /// <param name="typeId">The unique identifier for the message type.</param>
        /// <returns>An instance of the network message, or <c>null</c> if the type identifier is not registered.</returns>
        public static INetworkMessage? CreateMessage(byte typeId)
        {
            return registry.TryGetValue(typeId, out var entry) ? entry.Create() : null;
        }

        static NetworkMessageRegistry()
        {
            RegisterCore<JoinRequestMessage>(2);
            RegisterCore<JoinAcceptedMessage>(3);
            RegisterCore<ReadinessUpdateMessage>(4);
            RegisterCore<JoinRejectedMessage>(6);
            RegisterCore<HeartbeatMessage>(7);
            RegisterCore<HeartbeatReplyMessage>(8);
            RegisterCore<GamerLeavingMessage>(9); // Graceful leave protocol
            RegisterCore<SessionStateMessage>(10); // Session state synchronization
            RegisterCore<GamerJoinedMessage>(11);
            RegisterCore<RemovedFromSessionMessage>(12);
            RegisterCore<PasswordChallengeMessage>(13);
        }
    }
}
