using Microsoft.Xna.Framework.Net;

namespace Microsoft.Xna.Framework.GamerServices
{
    /// <summary>
    /// What XNA games on Windows call to start gamer services and to keep them running, as an alternative to
    /// adding a <see cref="GamerServicesComponent"/>: <c>Initialize</c> once, then <c>Update</c> every frame.
    /// </summary>
    public static class GamerServicesDispatcher
    {
        /// <summary>
        /// Gets whether <see cref="Initialize"/> has been called.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets or sets the handle of the game window. XNA used it to place the Games for Windows LIVE
        /// interface; nothing here does, so it only remembers what it is given.
        /// </summary>
        public static IntPtr WindowHandle { get; set; }

        /// <summary>
        /// Raised by XNA when a Games for Windows LIVE title update is being installed. Never raised here; it is
        /// declared so code that subscribes compiles.
        /// </summary>
#pragma warning disable CS0067 // never raised: there is no title update to install
        public static event EventHandler<EventArgs>? InstallingTitleUpdate;
#pragma warning restore CS0067

        /// <summary>
        /// Starts gamer services.
        /// </summary>
        public static void Initialize(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            IsInitialized = true;
        }

        /// <summary>
        /// Updates gamer services and raises their events: an invitation accepted in the platform's own UI is
        /// raised here, on the game's thread. Call it every frame.
        /// </summary>
        public static void Update()
        {
            NetworkServiceProvider.DispatchInvites();
        }
    }
}
