using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.Android
{
    /// <summary>
    /// Android Play Games implementation of Guide sign-in.
    /// </summary>
    public sealed class AndroidSignInProvider : IGuideSignInProvider
    {
        public async Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await AndroidRuntime.SignInAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
