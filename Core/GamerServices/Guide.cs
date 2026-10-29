namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Contract for the sign-in UI shown by <see cref="Guide.ShowSignInAsync"/>.
	/// </summary>
	public interface IGuideSignInProvider
    {
        /// <summary>
        /// Shows the sign-in UI.
        /// </summary>
        /// <param name="paneCount">Number of sign-in panes (gamers) requested.</param>
        /// <param name="onlineOnly">Whether only online-capable sign-in is acceptable.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>True if the user signed in; otherwise false.</returns>
        Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default);
    }

	internal sealed class DefaultGuideSignInProvider : IGuideSignInProvider
    {
        public Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

	/// <summary>
	/// Provides access to platform services.
	/// </summary>
	public static class Guide
    {
        private static readonly object gate = new();
        private static bool isVisible;
        private static IGuideSignInProvider signInProvider = new DefaultGuideSignInProvider();

        /// <summary>
        /// Gets whether the current game is running in trial mode.
        /// </summary>
        public static bool IsTrialMode => false; // Mock implementation - not in trial mode

        /// <summary>
        /// Gets whether the Guide is visible.
        /// </summary>
        public static bool IsVisible
        {
            get
            {
                lock (gate)
                {
                    return isVisible;
                }
            }
        }

        /// <summary>
        /// Platform-auth provider used by sign-in APIs.
        /// Override from the platform composition root.
        /// </summary>
        public static IGuideSignInProvider SignInProvider
        {
            get
            {
                lock (gate)
                {
                    return signInProvider;
                }
            }
            set
            {
                lock (gate)
                {
                    signInProvider = value ?? throw new ArgumentNullException(nameof(value));
                }
            }
        }

        /// <summary>
        /// Gets whether screen saver is enabled.
        /// </summary>
        public static bool IsScreenSaverEnabled
        {
            get => false;
            set { /* Mock implementation */ }
        }

        /// <summary>
        /// Shows a message box to the user.
        /// </summary>
        public static async Task<int?> ShowMessageBoxAsync(
            string title,
            string text,
            IEnumerable<string> buttons,
            int focusButton,
            MessageBoxIcon icon,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(buttons);

            var buttonList = buttons as IList<string> ?? buttons.ToList();
            if (buttonList.Count == 0)
                return null;

            cancellationToken.ThrowIfCancellationRequested();

            SetGuideVisible(true);

            try
            {
                // a hop to the thread pool rather than Task.Yield(), which would resume on the caller's synchronization
                // context and so deadlock ShowMessageBox when it blocks that context
                await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);

                if (focusButton < 0 || focusButton >= buttonList.Count)
                    return 0;

                return focusButton;
            }
            finally
            {
                SetGuideVisible(false);
            }
        }

        /// <summary>
        /// Synchronous wrapper for ShowMessageBoxAsync.
        /// </summary>
        /// <remarks>
        /// Blocks the calling thread until the asynchronous version finishes, as XNA's blocking calls did. The framework's own awaits
        /// use <c>ConfigureAwait(false)</c>, so this does not deadlock on a thread with a synchronization context, but the thread is
        /// stalled meanwhile: prefer the asynchronous version where you can.
        /// </remarks>
        public static int? ShowMessageBox(
            string title,
            string text,
            IEnumerable<string> buttons,
            int focusButton,
            MessageBoxIcon icon)
        {
            return ShowMessageBoxAsync(title, text, buttons, focusButton, icon).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Shows the sign-in interface.
        /// </summary>
        public static async Task ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(paneCount, 1);

            SetGuideVisible(true);

            try
            {
                var provider = SignInProvider;
                var signedIn = await provider.ShowSignInAsync(paneCount, onlineOnly, cancellationToken).ConfigureAwait(false);
                SignedInGamer.Current.SetSignedInToLive(signedIn);
            }
            finally
            {
                SetGuideVisible(false);
            }
        }

        /// <summary>
        /// Shows the sign-in interface (synchronous version).
        /// </summary>
        /// <remarks>
        /// Blocks the calling thread until the asynchronous version finishes, as XNA's blocking calls did. The framework's own awaits
        /// use <c>ConfigureAwait(false)</c>, so this does not deadlock on a thread with a synchronization context, but the thread is
        /// stalled meanwhile: prefer the asynchronous version where you can.
        /// </remarks>
        public static void ShowSignIn(int paneCount, bool onlineOnly)
        {
            ShowSignInAsync(paneCount, onlineOnly).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Shows the marketplace.
        /// </summary>
        public static void ShowMarketplace(PlayerIndex playerIndex)
        {
            // Mock implementation
        }

        private static void SetGuideVisible(bool value)
        {
            lock (gate)
            {
                isVisible = value;
            }
        }
    }
}
