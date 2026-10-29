using System.Net;
using Microsoft.Xna.Framework.Net;

namespace DataPath;

/// <summary>
/// A host and some clients, all in this process but talking over real UDP sockets on the loopback, so it needs
/// no second machine. Clients join the host directly by address, so it does not depend on LAN discovery.
/// </summary>
public static class SelfTest
{
    public static async Task<List<NodeReport>> RunAsync(Options options, TextWriter? log = null)
    {
        options.SameProcess = true;
        var sessions = new List<NetworkSession>();
        try
        {
            NetworkSession host = options.Password == null
                ? await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, options.Clients + 1, 0, null)
                : await NetworkSession.CreateAsync(NetworkSessionType.SystemLink, 1, options.Clients + 1, 0, null, options.Password);
            sessions.Add(host);

            int port = await HostPortAsync(host);
            log?.WriteLine($"hosting {host.LocalGamers[0].Gamertag} on port {port}");

            AvailableNetworkSession target = Direct(host.LocalGamers[0].Gamertag, new IPEndPoint(IPAddress.Loopback, port), options);
            for (int i = 0; i < options.Clients; i++)
            {
                NetworkSession client = options.Password == null
                    ? await NetworkSession.JoinAsync(target)
                    : await NetworkSession.JoinAsync(target, options.Password);
                sessions.Add(client);
            }

            // everybody knows everybody before the test starts
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (sessions.Any(s => s.AllGamers.Count < options.Clients + 1))
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("the players did not all see each other");
                foreach (NetworkSession s in sessions)
                    s.Update();
                await Task.Delay(10);
            }

            NodeReport[] reports = await Task.WhenAll(sessions.Select(s => Run.RunAsync(s, options, options.Clients, log)));
            return reports.ToList();
        }
        finally
        {
            foreach (NetworkSession s in sessions)
                s.Dispose();
        }
    }

    /// <summary>A session at a known address, for joining without discovery.</summary>
    public static AvailableNetworkSession Direct(string hostGamertag, IPEndPoint address, Options options) =>
        new AvailableNetworkSession("datapath", hostGamertag, 1, options.Clients, 0, NetworkSessionType.SystemLink,
            new Dictionary<string, object>(), "direct-" + address, UdpTransport.ToPeer(address), options.Password != null);

    public static async Task<int> HostPortAsync(NetworkSession host)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (host.NetworkTransport is UdpTransport udp && udp.LocalEndPoint != null)
                return udp.LocalEndPoint.Port;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("the host's socket was never bound");
    }
}
