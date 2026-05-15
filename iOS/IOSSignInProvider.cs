using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.iOS
{
    /// <summary>
    /// iOS implementation of Guide sign-in.
    /// </summary>
    public sealed class IOSSignInProvider : IGuideSignInProvider
    {
        public async Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await IOSRuntime.SignInAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}