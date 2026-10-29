using Foundation;
using NUnit.Common;
using NUnitLite;
using UIKit;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// Runs the test assembly inside the iOS simulator or on a device. There is no test host to collect
    /// results, so the runner writes to the console; the last line is <c>MGNET_TESTS_DONE exit=N</c>
    /// (N is the number of failed tests, 0 when all passed) and the app exits with N.
    /// </summary>
    public class Application
    {
        static void Main(string[] args)
        {
            UIApplication.Main(args, null, typeof(AppDelegate));
        }
    }

    [Register("AppDelegate")]
    public class AppDelegate : UIApplicationDelegate
    {
        public override UIWindow Window { get; set; }

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            Window = new UIWindow(UIScreen.MainScreen.Bounds) { RootViewController = new UIViewController() };
            Window.MakeKeyAndVisible();

            // off the main thread: tests block, and the main thread must keep the app alive
            Task.Run(() =>
            {
                int exit;
                try
                {
                    // no console input on iOS: pass a reader, or NUnitLite touches Console.In and fails
                    exit = new AutoRun(typeof(AppDelegate).Assembly).Execute(
                        new[] { "--labels=All" }, new ExtendedTextWrapper(Console.Out), TextReader.Null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("The runner crashed: " + ex);
                    exit = -1;
                }

                Console.WriteLine("MGNET_TESTS_DONE exit=" + exit);
                Console.Out.Flush();
                Thread.Sleep(500);
                Environment.Exit(exit);
            });

            return true;
        }
    }
}
