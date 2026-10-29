using System.Net;
using Microsoft.Xna.Framework.Net;

namespace DataPath;

internal static class Program
{
    const string Usage = @"DataPath: a test of the data path (SendData and ReceiveData) between players.

  DataPath selftest [options]   a host and clients in this process, over real UDP on the loopback
  DataPath host     [options]   host a session on this network and wait for the clients
  DataPath join     [options]   join a host found on this network (or at --address)

Everyone sends everyone positions (unreliable), an in-order counter, numbered reliable events and one large
message, then says what it sent; each machine checks what arrived. The exit code is 0 for PASS, 1 for FAIL.

options
  --clients N     how many clients there are, not counting the host (give the same N on every machine; default 3)
  --seconds S     how long to send for (default 8)
  --loss F        drop this share (0 to 1) of what this machine sends
  --latency MS    hold back what this machine sends by this many milliseconds
  --password P    the session password (selftest uses 'datapath' unless --no-password)
  --no-password   selftest only: no password
  --address A[:P] join: connect to this host directly instead of looking for one
  --min-ratio F   the least share of positions that must arrive (default: worked out from --loss)";

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        string mode = args[0];
        var options = new Options();
        string? address = null;
        bool noPassword = false;
        try
        {
            for (int i = 1; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--clients": options.Clients = int.Parse(Next()); break;
                    case "--seconds": options.Seconds = double.Parse(Next()); break;
                    case "--loss": options.Loss = float.Parse(Next()); break;
                    case "--latency": options.LatencyMs = int.Parse(Next()); break;
                    case "--password": options.Password = Next(); break;
                    case "--no-password": noPassword = true; break;
                    case "--address": address = Next(); break;
                    case "--min-ratio": options.MinPositionRatio = double.Parse(Next()); break;
                    default: throw new ArgumentException($"unknown option {args[i]}");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            switch (mode)
            {
                case "selftest":
                    if (options.Password == null && !noPassword)
                        options.Password = "datapath";
                    return Print(await SelfTest.RunAsync(options, Console.Out));

                case "host":
                    return Print(new[] { await HostAsync(options) });

                case "join":
                    return Print(new[] { await JoinAsync(options, address) });

                default:
                    Console.Error.WriteLine($"unknown mode {mode}");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("the test could not run: " + ex.Message);
            return 2;
        }
    }

    static async Task<NodeReport> HostAsync(Options options)
    {
        using NetworkSession host = options.Password == null
            ? await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, options.Clients + 1, 0, null)
            : await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, options.Clients + 1, 0, null, options.Password);
        int port = await SelfTest.HostPortAsync(host);
        Console.WriteLine($"hosting {host.LocalGamers[0].Gamertag} on port {port}; waiting for {options.Clients} client(s)...");
        return await Run.RunAsync(host, options, options.Clients, Console.Out);
    }

    static async Task<NodeReport> JoinAsync(Options options, string? address)
    {
        AvailableNetworkSession target;
        if (address != null)
        {
            string[] parts = address.Split(':');
            int port = parts.Length > 1 ? int.Parse(parts[1]) : 31338;   // the LAN back-end's usual game port
            target = SelfTest.Direct("host", new IPEndPoint(IPAddress.Parse(parts[0]), port), options);
        }
        else
        {
            Console.WriteLine("looking for a host on this network...");
            AvailableNetworkSession? found = null;
            for (int i = 0; i < 15 && found == null; i++)
            {
                using AvailableNetworkSessionCollection sessions = await NetworkSession.FindAsync(NetworkSessionType.SystemLink, 1, null);
                found = sessions.FirstOrDefault();
            }
            target = found ?? throw new TimeoutException("no host was found");
            Console.WriteLine($"found {target.HostGamertag}");
        }

        using NetworkSession session = options.Password == null
            ? await NetworkSession.JoinAsync(target)
            : await NetworkSession.JoinAsync(target, options.Password);
        Console.WriteLine($"joined as {session.LocalGamers[0].Gamertag}; waiting for the host to begin...");
        return await Run.RunAsync(session, options, options.Clients, Console.Out);
    }

    static int Print(IEnumerable<NodeReport> reports)
    {
        Console.WriteLine();
        bool passed = true;
        foreach (NodeReport report in reports)
        {
            report.Print(Console.Out);
            passed &= report.Passed;
        }
        Console.WriteLine();
        Console.WriteLine(passed ? "PASS" : "FAIL");
        return passed ? 0 : 1;
    }
}
