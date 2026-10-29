using System.Text;
using Android.App;
using Android.OS;
using Android.Util;
using NUnit.Common;
using NUnitLite;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Runs the test assembly inside the emulator or device and reports through logcat (tag
    /// <c>MGNET_TESTS</c>), because there is no test host to collect results on Android. The last line
    /// is <c>MGNET_TESTS_DONE exit=N</c>; N is the number of failed tests, 0 when all passed.
    /// </summary>
    [Activity(Label = "MGNet Tests", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private const string Tag = "MGNET_TESTS";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // off the UI thread: tests block and the UI thread must stay responsive
            new Thread(RunTests) { IsBackground = false }.Start();
        }

        private static void RunTests()
        {
            int exit;
            try
            {
                var runner = new AutoRun(typeof(MainActivity).Assembly);
                exit = runner.Execute(new[] { "--labels=All" }, new ExtendedTextWrapper(new LogcatWriter()), TextReader.Null);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, "The runner crashed: " + ex);
                exit = -1;
            }

            Log.Info(Tag, "MGNET_TESTS_DONE exit=" + exit);
        }

        // one logcat line per line of runner output
        private sealed class LogcatWriter : TextWriter
        {
            private readonly StringBuilder line = new StringBuilder();

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n')
                    Flush();
                else if (value != '\r')
                    line.Append(value);
            }

            public override void Flush()
            {
                if (line.Length == 0)
                    return;
                Log.Info(Tag, line.ToString());
                line.Clear();
            }
        }
    }
}
