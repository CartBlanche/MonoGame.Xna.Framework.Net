using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// <see cref="NetworkSession.GameId"/> keeps two games on the same LAN from listing (and so joining) each
    /// other's sessions. Before it, every game that used the library showed up in every other game's list.
    /// </summary>
    [TestFixture]
    public class GameIdTests
    {
        SessionHarness lan;

        [SetUp]
        public void SetUp() => lan = new SessionHarness();

        [TearDown]
        public void TearDown()
        {
            NetworkSession.GameId = string.Empty;
            lan.Dispose();
        }

        static async Task<List<string>> FindGamertagsAsync()
        {
            using (AvailableNetworkSessionCollection found =
                await NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, new Dictionary<string, object>()))
                return found.Select(s => s.HostGamertag).ToList();
        }

        [Test]
        public async Task ASessionOfTheSameGame_IsFound_AndCanBeJoined()
        {
            NetworkSession.GameId = "GameA";
            NetworkSession host = await lan.StartHostAsync();

            NetworkSession client = await lan.JoinClientAsync();

            Assert.That(client.AllGamers.Count, Is.EqualTo(2));
            Assert.That(host.AllGamers.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ASessionOfAnotherGame_IsNotListed()
        {
            NetworkSession.GameId = "GameA";
            NetworkSession host = await lan.StartHostAsync();
            string hostName = SessionHarness.LocalOf(host).Gamertag;

            NetworkSession.GameId = "GameB";
            List<string> seen = await FindGamertagsAsync();

            Assert.That(seen, Does.Not.Contain(hostName), "GameB listed a session of GameA");

            // and it is still there for its own game
            NetworkSession.GameId = "GameA";
            Assert.That(await FindGamertagsAsync(), Does.Contain(hostName));
        }

        [Test]
        public async Task AGameWithNoId_DoesNotListAGamesSession_AndTheOtherWayRound()
        {
            NetworkSession.GameId = "GameA";
            NetworkSession named = await lan.StartHostAsync();
            string namedHost = SessionHarness.LocalOf(named).Gamertag;

            NetworkSession.GameId = string.Empty;
            NetworkSession unnamed = await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, 4, 0, new Dictionary<string, object>());
            try
            {
                string unnamedHost = SessionHarness.LocalOf(unnamed).Gamertag;
                List<string> seenWithNoId = await FindGamertagsAsync();
                Assert.That(seenWithNoId, Does.Contain(unnamedHost));
                Assert.That(seenWithNoId, Does.Not.Contain(namedHost));

                NetworkSession.GameId = "GameA";
                List<string> seenAsGameA = await FindGamertagsAsync();
                Assert.That(seenAsGameA, Does.Contain(namedHost));
                Assert.That(seenAsGameA, Does.Not.Contain(unnamedHost));
            }
            finally
            {
                unnamed.Dispose();
            }
        }

        [Test]
        public async Task ASessionKeepsTheGameIdItWasCreatedWith()
        {
            NetworkSession.GameId = "GameA";
            NetworkSession host = await lan.StartHostAsync();
            string hostName = SessionHarness.LocalOf(host).Gamertag;

            // changing it afterwards does not change what the existing session announces
            NetworkSession.GameId = "GameB";
            await Task.Delay(2000);

            NetworkSession.GameId = "GameA";
            Assert.That(await FindGamertagsAsync(), Does.Contain(hostName));
            NetworkSession.GameId = "GameB";
            Assert.That(await FindGamertagsAsync(), Does.Not.Contain(hostName));
        }

        [TestCase("NetRumble", "NetRumble")]
        [TestCase("  Net Rumble  ", "Net Rumble")]
        [TestCase("a:b:c", "a_b_c")]
        [TestCase(null, "")]
        public void TheIdIsTidied_SoItCannotBreakTheAnnouncement(string given, string expected)
        {
            NetworkSession.GameId = given;
            Assert.That(NetworkSession.GameId, Is.EqualTo(expected));
        }

        [Test]
        public void AVeryLongIdIsCutTo64Characters()
        {
            NetworkSession.GameId = new string('x', 200);
            Assert.That(NetworkSession.GameId.Length, Is.EqualTo(64));
        }
    }
}
