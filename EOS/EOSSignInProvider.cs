using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net.EOS
{
    /// <summary>
    /// EOS implementation of Guide sign-in.
    /// </summary>
    public sealed class EOSSignInProvider : IGuideSignInProvider
    {
        public async Task<bool> ShowSignInAsync(int paneCount, bool onlineOnly, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await EOSRuntime.SignInAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
