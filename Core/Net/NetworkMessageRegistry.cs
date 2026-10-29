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

        private static readonly ConcurrentDictionary<byte, Entry> registry = new();

        /// <summary>
        /// Registers a network message type with the specified type identifier.
        /// Registering the same type under the same identifier again is harmless.
        /// </summary>
        /// <typeparam name="T">The type of the network message to register. Must implement <see cref="INetworkMessage"/> and have a parameterless constructor.</typeparam>
        /// <param name="typeId">The unique identifier for the message type.</param>
        /// <exception cref="InvalidOperationException">The identifier already belongs to a different type, which includes the framework's own messages.</exception>
        public static void Register<T>(byte typeId) where T : INetworkMessage, new()
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
        public static INetworkMessage CreateMessage(byte typeId)
        {
            return registry.TryGetValue(typeId, out var entry) ? entry.Create() : null;
        }

        static NetworkMessageRegistry()
        {
            Register<JoinRequestMessage>(2);
            Register<JoinAcceptedMessage>(3);
            Register<ReadinessUpdateMessage>(4);
            Register<JoinRejectedMessage>(6);
            Register<HeartbeatMessage>(7);
            Register<HeartbeatReplyMessage>(8);
            Register<GamerLeavingMessage>(9); // Graceful leave protocol
            Register<SessionStateMessage>(10); // Session state synchronization
            Register<GamerJoinedMessage>(11);
            Register<RemovedFromSessionMessage>(12);
            Register<PasswordChallengeMessage>(13);
        }
    }
}
