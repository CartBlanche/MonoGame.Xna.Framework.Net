namespace DataPath;

public sealed class Options
{
    /// <summary>How many clients the host expects (the self-test starts them itself).</summary>
    public int Clients { get; set; } = 3;

    /// <summary>How long the players send data for.</summary>
    public double Seconds { get; set; } = 8;

    /// <summary>The share of datagrams this machine drops from what it sends, 0 to 1 (SimulatedPacketLoss).</summary>
    public float Loss { get; set; }

    /// <summary>How long this machine holds back what it sends (SimulatedLatency).</summary>
    public int LatencyMs { get; set; }

    /// <summary>The session password, if any.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// The fewest positions (as a share of what was sent) that must get through. Positions are unreliable, so
    /// some are lost on purpose; this only catches a link that has stopped carrying them. Null works it out
    /// from <see cref="Loss"/>.
    /// </summary>
    public double? MinPositionRatio { get; set; }

    /// <summary>True when every player runs in this process, so send and receive times can be compared.</summary>
    public bool SameProcess { get; set; }

    public int PositionHz { get; set; } = 20;
    public int SequencedHz { get; set; } = 10;
    public int EventHz { get; set; } = 2;

    internal double RequiredPositionRatio =>
        MinPositionRatio ?? 0.5 * Math.Pow(1 - Math.Clamp(Loss, 0f, 0.9f), 2);   // up to two lossy hops: a client to a client goes through the host
}
