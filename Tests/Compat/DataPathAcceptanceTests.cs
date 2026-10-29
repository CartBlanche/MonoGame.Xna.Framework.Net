using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Runs the data-path sample's self-test: a host and clients sending each other positions (unreliable), an in-order
    /// stream, reliable events and a large message, with a lossy and slow network injected, and every one of them
    /// checking what arrived. The sample uses only the public XNA-shaped API, like everything here.
    /// </summary>
    [TestFixture]
    public class DataPathAcceptanceTests
    {
        [Test]
        public async Task EveryoneReceivesEverythingThatMustArrive_OverALossyDelayedNetwork()
        {
            var options = new global::DataPath.Options
            {
                Clients = 2,
                Seconds = 5,
                Loss = 0.1f,
                LatencyMs = 40,
                Password = "datapath"      // so the data travels over the protected links
            };

            List<global::DataPath.NodeReport> reports = await global::DataPath.SelfTest.RunAsync(options);

            Assert.That(reports, Has.Count.EqualTo(3), "a host and two clients");
            string failures = string.Join("\n", reports.SelectMany(r => r.Failures.Select(f => $"{r.Name}: {f}")));
            Assert.That(failures, Is.Empty);
        }
    }
}
