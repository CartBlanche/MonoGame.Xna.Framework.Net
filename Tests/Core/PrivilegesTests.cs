using Microsoft.Xna.Framework.GamerServices;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.Tests
{
    [TestFixture]
    public class PrivilegesTests
    {
        [TearDown]
        public void TearDown() => SignedInGamer.Current.SetSignedInToLive(false);

        [Test]
        public void ASignedInGamer_AlwaysHasPrivileges_NeverNull()
        {
            Assert.That(SignedInGamer.Current.Privileges, Is.Not.Null);
        }

        [Test]
        public void OnlineSessions_AreAllowedExactlyWhileSignedInToLive()
        {
            SignedInGamer.Current.SetSignedInToLive(false);
            Assert.That(SignedInGamer.Current.Privileges.AllowOnlineSessions, Is.False);

            SignedInGamer.Current.SetSignedInToLive(true);
            Assert.That(SignedInGamer.Current.Privileges.AllowOnlineSessions, Is.True);

            SignedInGamer.Current.SetSignedInToLive(false);
            Assert.That(SignedInGamer.Current.Privileges.AllowOnlineSessions, Is.False);
        }
    }
}
