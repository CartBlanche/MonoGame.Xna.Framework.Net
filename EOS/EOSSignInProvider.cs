using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// EOS implementation of Guide sign-in.
    /// </summary>
    public sealed class EOSSignInProvider : IGuideSignInProvider
    {
        /// <summary>
        /// Signs in through <see cref="EOSRuntime.SignInAsync"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="paneCount"/> and <paramref name="onlineOnly"/> are ignored.
        /// </remarks>
        public async Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await EOSRuntime.SignInAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
