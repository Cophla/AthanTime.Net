using System.Diagnostics;
using AthanTime.Net.Services;

namespace AthanTime.Tests;

/// <summary>Checks the Release guard/activation channel and Debug exception across isolated processes without desktop/profile access.</summary>
public sealed class SingleInstanceTests
{
    [Fact]
    public async Task FailedActivationIsReportedAndLaterRequestsStillWork()
    {
        var name = "athan-activate-failure-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            await owner.StandardInput.WriteLineAsync("fail-next");
            Assert.Equal("ready", await ReadStatus(owner));
            using var client = Start(name, activationMode: "activate");
            try
            {
                Assert.Equal($"target:{owner.Id}", await ReadStatus(client));
                Assert.Equal("unavailable", await ReadStatus(client));
                await Exited(client);
            }
            finally { Stop(client); }
            Assert.Equal("error:InvalidOperationException", await ReadStatus(owner));
            await Activate(name, owner.Id);
            Assert.Equal("activated", await ReadStatus(owner));
            using var duplicate = Start(name);
            Assert.Equal("duplicate", await ReadStatus(duplicate));
            await Exited(duplicate);
        }
        finally { Stop(owner); }
    }

    [Fact]
    public async Task ReadyOwnerReceivesRepeatedActivationsWithoutReleasingItsLease()
    {
        var name = "athan-activate-ready-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            for (int index = 0; index < 3; index++)
            {
                await Activate(name, owner.Id);
                Assert.Equal("activated", await ReadStatus(owner));
                Assert.False(owner.HasExited);
            }
            using var duplicate = Start(name);
            Assert.Equal("duplicate", await ReadStatus(duplicate));
            await Exited(duplicate);
        }
        finally { Stop(owner); }
    }

    [Fact]
    public async Task StartupRequestsCoalesceUntilTheWindowHandlerIsReady()
    {
        var name = "athan-activate-startup-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner", delayed: true);
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            for (int index = 0; index < 3; index++) await Activate(name, owner.Id);
            await owner.StandardInput.WriteLineAsync("ready");
            Assert.Equal("activated", await ReadStatus(owner));
            await owner.StandardInput.WriteLineAsync();
            await Exited(owner);
            Assert.Equal("", await owner.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally { Stop(owner); }
    }

    [Fact]
    public async Task ConcurrentActivationsRemainWithinTheirApplicationIdentity()
    {
        var name = "athan-activate-race-" + Guid.NewGuid().ToString("N");
        var otherName = "athan-activate-other-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        using var other = Start(otherName, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            Assert.Equal("acquired", await ReadStatus(other));
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Activate(name, owner.Id)));
            for (int index = 0; index < 4; index++) Assert.Equal("activated", await ReadStatus(owner));
            await Activate(otherName, other.Id);
            Assert.Equal("activated", await ReadStatus(other));
            await other.StandardInput.WriteLineAsync();
            await Exited(other);
            Assert.Equal("", await other.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally { Stop(owner); Stop(other); }
    }

    [Fact]
    public async Task ClosingActivationKeepsTheLeaseUntilTheOwnerExits()
    {
        var name = "athan-activate-stop-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            await owner.StandardInput.WriteLineAsync("stop");
            Assert.Equal("stopped", await ReadStatus(owner));
            using var client = Start(name, activationMode: "activate");
            Assert.Equal("unavailable", await ReadStatus(client));
            await Exited(client);
            using var duplicate = Start(name);
            Assert.Equal("duplicate", await ReadStatus(duplicate));
            await Exited(duplicate);
        }
        finally { Stop(owner); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationEndpointCanRestartAfterNormalOrForcedExit(bool terminate)
    {
        var name = "athan-activate-restart-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            if (terminate) owner.Kill(); else await owner.StandardInput.WriteLineAsync();
            await Exited(owner);
            using var restarted = Start(name, activationMode: "activation-owner");
            try
            {
                Assert.Equal("acquired", await ReadStatus(restarted));
                await Activate(name, restarted.Id);
                Assert.Equal("activated", await ReadStatus(restarted));
            }
            finally { Stop(restarted); }
        }
        finally { Stop(owner); }
    }

    [Fact]
    public async Task StartupActivatesTheOwnerOnlyInRelease()
    {
        var name = "athan-activate-policy-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name, activationMode: "activation-owner");
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            using var launch = Start(name, activationMode: "startup-activate");
            try
            {
#if DEBUG
                Assert.Equal("acquired", await ReadStatus(launch));
                await launch.StandardInput.WriteLineAsync();
#else
                Assert.Equal("duplicate", await ReadStatus(launch));
                Assert.Equal("activated", await ReadStatus(owner));
#endif
                await Exited(launch);
                await owner.StandardInput.WriteLineAsync();
                await Exited(owner);
                Assert.Equal("", await owner.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
            }
            finally { Stop(launch); }
        }
        finally { Stop(owner); }
    }

    [Fact]
    public async Task CancelledRequestDoesNotCreateAnOwner()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False(await SingleInstanceGuard.RequestActivationAsync("athan-cancelled-" + Guid.NewGuid().ToString("N"),
            cancellation.Token, _ => throw new InvalidOperationException("No server should be contacted.")));
    }

    private static async Task Activate(string name, int processId)
    {
        using var launch = Start(name, activationMode: "activate");
        try
        {
            Assert.Equal($"target:{processId}", await ReadStatus(launch));
            Assert.Equal("activated", await ReadStatus(launch));
            await Exited(launch);
            Assert.Equal(0, launch.ExitCode);
        }
        finally { Stop(launch); }
    }

    /// <summary>Debug startups can coexist; Release retains one lease owner despite a Development environment.</summary>
    [Fact]
    public async Task StartupPolicyMatchesBuildConfiguration()
    {
        var name = "athan-startup-policy-" + Guid.NewGuid().ToString("N");
        using var first = Start(name, startup: true);
        try
        {
            Assert.Equal("acquired", await ReadStatus(first));
            using var second = Start(name, startup: true);
            try
            {
#if DEBUG
                Assert.Equal("acquired", await ReadStatus(second));
                await second.StandardInput.WriteLineAsync();
#else
                Assert.Equal("duplicate", await ReadStatus(second));
#endif
                await Exited(second);
                Assert.Equal(0, second.ExitCode);
                Assert.False(first.HasExited);
                await first.StandardInput.WriteLineAsync();
                await Exited(first);
                Assert.Equal(0, first.ExitCode);
            }
            finally { Stop(second); }
        }
        finally { Stop(first); }
    }

    /// <summary>Debug startup neither takes an enforced lease nor waits for one, regardless of launch order.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupAndEnforcedLeaseRespectBuildPolicy(bool startupFirst)
    {
        var name = "athan-startup-coexist-" + Guid.NewGuid().ToString("N");
        using var first = Start(name, startup: startupFirst);
        try
        {
            Assert.Equal("acquired", await ReadStatus(first));
            using var second = Start(name, startup: !startupFirst);
            try
            {
#if DEBUG
                Assert.Equal("acquired", await ReadStatus(second));
                await second.StandardInput.WriteLineAsync();
#else
                Assert.Equal("duplicate", await ReadStatus(second));
#endif
                await Exited(second);
                Assert.Equal(0, second.ExitCode);
                await first.StandardInput.WriteLineAsync();
                await Exited(first);
                Assert.Equal(0, first.ExitCode);
            }
            finally { Stop(second); }
        }
        finally { Stop(first); }
    }

    /// <summary>A duplicate exits and the lease becomes available after either graceful exit or termination.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateIsRejectedAndExitAllowsRestart(bool terminate)
    {
        var name = "athan-instance-test-" + Guid.NewGuid().ToString("N");
        using var owner = Start(name);
        try
        {
            Assert.Equal("acquired", await ReadStatus(owner));
            using var duplicate = Start(name);
            try
            {
                Assert.Equal("duplicate", await ReadStatus(duplicate));
                await Exited(duplicate);
                Assert.Equal(0, duplicate.ExitCode);
                Assert.False(owner.HasExited);
            }
            finally { Stop(duplicate); }

            if (terminate) owner.Kill();
            else await owner.StandardInput.WriteLineAsync();
            await Exited(owner);

            using var restarted = Start(name);
            try
            {
                Assert.Equal("acquired", await ReadStatus(restarted));
                await restarted.StandardInput.WriteLineAsync();
                await Exited(restarted);
                Assert.Equal(0, restarted.ExitCode);
            }
            finally { Stop(restarted); }
        }
        finally { Stop(owner); }
    }

    /// <summary>Concurrent launches elect exactly one owner without a check-then-create race.</summary>
    [Fact]
    public async Task SimultaneousLaunchesHaveExactlyOneOwner()
    {
        var name = "athan-instance-race-" + Guid.NewGuid().ToString("N");
        var processes = Enumerable.Range(0, 4).Select(_ => Start(name)).ToArray();
        try
        {
            var statuses = await Task.WhenAll(processes.Select(ReadStatus));
            Assert.Equal(1, statuses.Count(status => status == "acquired"));
            Assert.Equal(3, statuses.Count(status => status == "duplicate"));
            for (var index = 0; index < processes.Length; index++)
                if (statuses[index] == "acquired") await processes[index].StandardInput.WriteLineAsync();
            await Task.WhenAll(processes.Select(Exited));
            Assert.All(processes, process => Assert.Equal(0, process.ExitCode));
        }
        finally
        {
            foreach (var process in processes) { Stop(process); process.Dispose(); }
        }
    }

    /// <summary>Isolated test identities never block other application identities.</summary>
    [Fact]
    public async Task DifferentIdentitiesRemainIndependent()
    {
        using var first = Start("athan-independent-" + Guid.NewGuid().ToString("N"));
        using var second = Start("athan-independent-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal("acquired", await ReadStatus(first));
            Assert.Equal("acquired", await ReadStatus(second));
        }
        finally { Stop(first); Stop(second); }
    }

    /// <summary>An abandoned mutex with an outstanding handle can be acquired again without a crash.</summary>
    [Fact]
    public async Task AbandonedOwnerCanBeRecovered()
    {
        var name = "athan-abandoned-" + Guid.NewGuid().ToString("N");
        using var recovered = Start(name, abandon: true);
        try
        {
            Assert.Equal("acquired", await ReadStatus(recovered));
            using var duplicate = Start(name);
            try
            {
                Assert.Equal("duplicate", await ReadStatus(duplicate));
                await Exited(duplicate);
                Assert.Equal(0, duplicate.ExitCode);
            }
            finally { Stop(duplicate); }
            await recovered.StandardInput.WriteLineAsync();
            await Exited(recovered);
            Assert.Equal(0, recovered.ExitCode);
        }
        finally { Stop(recovered); }
    }

    private static Process Start(string name, bool abandon = false, bool startup = false, string? activationMode = null, bool delayed = false)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SingleInstanceProbe", "SingleInstanceProbe.dll"));
        start.ArgumentList.Add(name);
        if (abandon) start.ArgumentList.Add("abandon");
        if (activationMode is not null) start.ArgumentList.Add(activationMode);
        if (delayed) start.ArgumentList.Add("delayed");
        if (startup)
        {
            start.ArgumentList.Add("startup");
            // Release enforcement must depend on its build, not developer host variables.
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        }
        return Process.Start(start)!;
    }

    private static async Task<string?> ReadStatus(Process process) =>
        await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

    private static Task Exited(Process process) => process.WaitForExitAsync(TestContext.Current.CancellationToken)
        .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

    private static void Stop(Process process)
    {
        if (process.HasExited) return;
        process.Kill();
        process.WaitForExit();
    }
}
