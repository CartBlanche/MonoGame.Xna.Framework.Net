using System.Runtime.InteropServices;
using Epic.OnlineServices;
using NUnit.Framework;
using Platform = Epic.OnlineServices.Platform;

namespace Microsoft.Xna.Framework.Net.EOS.Tests
{
    /// <summary>
    /// Needs no Epic account and no network: it only checks that the EOS native library can be found and
    /// loaded on this machine, which is the first thing that goes wrong when a build is run from source.
    /// </summary>
    [TestFixture]
    public class EOSNativeLibraryTests
    {
        [Test]
        public void TheNativeSdk_LoadsOnThisMachine()
        {
            EOSClient.InstallNativeResolverIfNeeded();

            var options = new Platform.InitializeOptions { ProductName = "MonoGame.Xna.Framework.Net.Tests", ProductVersion = "1.0.0" };
            Result result;
            try
            {
                result = Platform.PlatformInterface.Initialize(ref options);
            }
            catch (DllNotFoundException ex)
            {
                Assert.Fail($"The EOS native library was not found on {RuntimeInformation.RuntimeIdentifier}: {ex.Message}");
                return;
            }

            Assert.That(result, Is.AnyOf(Result.Success, Result.AlreadyConfigured));
        }
    }
}
