using System.Text;

namespace DataPath;

/// <summary>What one machine saw from one other player.</summary>
internal sealed class PeerStats
{
    public PeerStats(string id, string name) { Id = id; Name = name; }

    public string Id { get; }
    public string Name { get; }

    public int Positions;
    public int PositionDuplicates;
    public int PositionsWrong;
    public readonly HashSet<uint> PositionsSeen = new();
    public double LatencyMinMs = double.MaxValue;
    public double LatencySumMs;
    public int LatencySamples;

    public int Sequenced;
    public int SequencedBackwards;
    public uint LastSequenced;

    public int Events;
    public int EventsOutOfOrder;
    public uint NextEvent = 1;

    public int Blobs;
    public int BlobsCorrupt;

    public bool DoneReceived;
    public uint SentPositions, SentSequenced, SentEvents, SentBlobs;
}

/// <summary>The verdict of one machine, after everyone has said they are done.</summary>
public sealed class NodeReport
{
    public NodeReport(string name, bool isHost) { Name = name; IsHost = isHost; }

    public string Name { get; }
    public bool IsHost { get; }
    public List<string> Lines { get; } = new();
    public List<string> Failures { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Passed => Failures.Count == 0;

    internal void Evaluate(IEnumerable<PeerStats> peers, IReadOnlyCollection<(string Id, string Name)> expectedPeers, Options options)
    {
        var byId = peers.ToDictionary(p => p.Id);

        if (expectedPeers.Count == 0)
            Failures.Add("there was nobody else in the session");

        foreach ((string id, string name) in expectedPeers)
        {
            if (!byId.TryGetValue(id, out PeerStats? p) || !p.DoneReceived)
            {
                Failures.Add($"nothing finished arriving from {name}: its Done never came");
                if (p == null)
                    continue;
            }

            var line = new StringBuilder();
            double ratio = p.SentPositions == 0 ? 0 : (double)p.Positions / p.SentPositions;
            line.Append($"from {p.Name}: positions {p.Positions}/{p.SentPositions} ({ratio:P0})");
            if (p.PositionDuplicates > 0) line.Append($", {p.PositionDuplicates} duplicated");
            line.Append($" | in-order {p.Sequenced}/{p.SentSequenced}");
            line.Append($" | events {p.Events}/{p.SentEvents}");
            line.Append($" | blobs {p.Blobs}/{p.SentBlobs}");
            if (p.LatencySamples > 0)
                line.Append($" | one-way {p.LatencyMinMs:F0} to {p.LatencySumMs / p.LatencySamples:F0} ms (min, avg)");
            Lines.Add(line.ToString());

            // what must never happen, whatever the network does
            if (p.PositionsWrong > 0) Failures.Add($"{p.Name}: {p.PositionsWrong} positions arrived with the wrong value");
            if (p.SequencedBackwards > 0) Failures.Add($"{p.Name}: the in-order stream went backwards {p.SequencedBackwards} times");
            if (p.EventsOutOfOrder > 0) Failures.Add($"{p.Name}: {p.EventsOutOfOrder} reliable events were duplicated, missing or out of order");
            if (p.DoneReceived && p.Events != p.SentEvents) Failures.Add($"{p.Name}: {p.Events} of {p.SentEvents} reliable events arrived");
            if (p.BlobsCorrupt > 0) Failures.Add($"{p.Name}: {p.BlobsCorrupt} large messages arrived changed");
            if (p.DoneReceived && p.Blobs != p.SentBlobs) Failures.Add($"{p.Name}: {p.Blobs} of {p.SentBlobs} large messages arrived");

            // positions and the in-order stream may be lost, but not all of them
            if (p.DoneReceived && p.SentPositions >= 20 && ratio < options.RequiredPositionRatio)
                Failures.Add($"{p.Name}: only {ratio:P0} of the positions arrived (at least {options.RequiredPositionRatio:P0} expected)");
            if (p.PositionDuplicates > 0)
                Warnings.Add($"{p.Name}: {p.PositionDuplicates} positions arrived twice (the network duplicated them)");

            // a machine that holds back what it sends must show it on the other side (only when clocks can be compared)
            if (options.SameProcess && options.LatencyMs > 0 && p.LatencySamples > 0 && p.LatencyMinMs < options.LatencyMs * 0.8)
                Failures.Add($"{p.Name}: positions arrived after {p.LatencyMinMs:F0} ms, faster than the simulated {options.LatencyMs} ms");
        }
    }

    public void Print(TextWriter output)
    {
        output.WriteLine($"{(IsHost ? "host" : "client")} {Name}: {(Passed ? "PASS" : "FAIL")}");
        foreach (string line in Lines) output.WriteLine("  " + line);
        foreach (string w in Warnings) output.WriteLine("  warning: " + w);
        foreach (string f in Failures) output.WriteLine("  FAILED: " + f);
    }
}
