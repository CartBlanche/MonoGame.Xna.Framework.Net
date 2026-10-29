# DataPath

A small acceptance test for the data path (`SendData` / `ReceiveData`), written against the public XNA-shaped API only.
It is a console app, so it can run in one process or on real machines, and it exits with 0 for PASS and 1 for FAIL.

Every player sends every other player, for a few seconds:

| What | How it is sent | What the receiver checks |
|------|----------------|--------------------------|
| Positions, 20 a second | unreliable | each value is exactly what the sender computed; some may be lost, not most |
| A counter, 10 a second | in order, not reliable | it never goes backwards |
| Numbered events, 2 a second | reliable and in order | each arrives once, in order, with none missing |
| One 60 KB message | reliable and in order (split into pieces) | it arrives identical |

Then each player says what it sent, and every machine checks what arrived against that. Data between two clients goes through
the host, so this also covers the relay.

```
dotnet run -- selftest                              a host and 3 clients in this process, over real UDP on the loopback
dotnet run -- selftest --loss 0.15 --latency 60     the same, with every machine dropping 15% of what it sends and delaying the rest by 60 ms
dotnet run -- host --clients 2 --password secret    on one machine: host, and wait for 2 clients
dotnet run -- join --clients 2 --password secret    on each of the others: find the host on the network and join
dotnet run -- join --clients 2 --address 192.168.1.20
```

`--clients N` is the number of clients, not counting the host; give the same N on every machine. `--loss` and `--latency`
apply to what the machine they are given to sends (`SimulatedPacketLoss` and `SimulatedLatency`), so give them to the
machines you want to behave badly.

The compat test suite runs the self-test (`Tests/Compat/DataPathAcceptanceTests.cs`). To try another back-end, set it with
`NetworkServiceProvider.SetBackend` before the session is created; the sample does not do that itself.
