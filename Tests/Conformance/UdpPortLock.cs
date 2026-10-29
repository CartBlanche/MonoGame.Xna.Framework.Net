using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

/// <summary>
/// The SystemLink tests all use the same fixed UDP ports (31337 and 31338), so two test assemblies running at
/// once (as a solution-wide <c>dotnet test</c> does) find and join each other's sessions. Every assembly that
/// is linked to this file takes a machine-wide lock for the whole run, so the assemblies run one at a time
/// however they are started.
/// </summary>
[SetUpFixture]
public sealed class UdpPortLock
{
    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "mgnet-tests-udp-ports.lock");
    private FileStream? held;

    [OneTimeSetUp]
    public void Acquire()
    {
        var give_up = DateTime.UtcNow.AddMinutes(15);
        while (true)
        {
            try
            {
                held = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < give_up)
            {
                Thread.Sleep(250);
            }
        }
    }

    [OneTimeTearDown]
    public void Release()
    {
        held?.Dispose();
        held = null;
    }
}
