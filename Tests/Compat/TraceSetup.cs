using System.Diagnostics;
using NUnit.Framework;

namespace Microsoft.Xna.Framework.Net.CompatTests
{
    /// <summary>
    /// Sends Debug.WriteLine output (the library's [JOIN]/[DISCOVERY] traces, Debug builds only) to the console.
    /// </summary>
    [SetUpFixture]
    public class TraceSetup
    {
        [OneTimeSetUp]
        public void Attach() => Trace.Listeners.Add(new ConsoleTraceListener());
    }
}
