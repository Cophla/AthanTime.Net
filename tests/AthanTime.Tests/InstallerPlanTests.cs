using InstallerBuilder;

namespace AthanTime.Tests;

/// <summary>Checks native host restrictions and cross-platform archive naming.</summary>
public sealed class InstallerPlanTests
{
    [Theory]
    [InlineData(BuildHost.Windows, "win-x64", "AthanTime-1.2.3-Windows-x64-Setup.exe")]
    [InlineData(BuildHost.Linux, "linux-x64", "AthanTime-1.2.3-Linux-x64.deb")]
    [InlineData(BuildHost.Linux, "linux-arm64", "AthanTime-1.2.3-Linux-arm64.deb")]
    [InlineData(BuildHost.MacOS, "osx-arm64", "AthanTime-1.2.3-macOS-AppleSilicon-arm64.dmg")]
    [InlineData(BuildHost.MacOS, "osx-x64", "AthanTime-1.2.3-macOS-Intel-x64.dmg")]
    public void NativePlan_UsesExpectedFilename(BuildHost host, string runtime, string expected)
    {
        var plan = InstallerPlan.Create("NATIVE", runtime.ToUpperInvariant(), "1.2.3", host);
        Assert.Equal(expected, plan.FileName);
    }

    [Fact]
    public void PortablePlan_AllowsOtherTargetOperatingSystems()
    {
        var plan = InstallerPlan.Create("PoRtAbLe", "osx-x64", "1.2.3", BuildHost.Windows);
        Assert.Equal("AthanTime-1.2.3-macOS-Intel-x64-Portable.zip", plan.FileName);
    }

    /// <summary>Portable names identify every target consistently, regardless of the build host.</summary>
    [Theory]
    [InlineData("win-x64", "Windows-x64")]
    [InlineData("linux-x64", "Linux-x64")]
    [InlineData("linux-arm64", "Linux-arm64")]
    [InlineData("osx-x64", "macOS-Intel-x64")]
    [InlineData("osx-arm64", "macOS-AppleSilicon-arm64")]
    public void PortableNamesIdentifyPlatformAndKeepPrerelease(string runtime, string platform)
    {
        foreach (var host in Enum.GetValues<BuildHost>())
        {
            var plan = InstallerPlan.Create("portable", runtime, "1.2.3-beta.4", host);
            Assert.Equal($"AthanTime-1.2.3-beta.4-{platform}-Portable.zip", plan.FileName);
            Assert.Matches(@"\A[A-Za-z0-9._-]+\z", plan.FileName);
        }
    }

    [Theory]
    [InlineData("native", "osx-x64", "1.2.3")]
    [InlineData("native", "linux-x64", "1.2.3")]
    [InlineData("portable", "unknown", "1.2.3")]
    [InlineData("portable", "win-x64", "../unsafe")]
    public void InvalidPlans_AreRejected(string format, string runtime, string version) =>
        Assert.Throws<ArgumentException>(() => InstallerPlan.Create(format, runtime, version, BuildHost.Windows));

    /// <summary>Windows signing applies to Windows targets only, including portable archives.</summary>
    [Theory]
    [InlineData("native", "win-x64", true)]
    [InlineData("portable", "win-x64", true)]
    [InlineData("portable", "linux-x64", false)]
    [InlineData("portable", "linux-arm64", false)]
    [InlineData("portable", "osx-x64", false)]
    [InlineData("portable", "osx-arm64", false)]
    public void SigningIntentFollowsTargetRuntime(string format, string runtime, bool expected)
    {
        var plan = InstallerPlan.Create(format, runtime, "1.2.3", BuildHost.Windows, windowsSigningRequested: true);
        Assert.Equal(expected, plan.SignWindowsBinaries);
        Assert.False(InstallerPlan.Create(format, runtime, "1.2.3", BuildHost.Windows).SignWindowsBinaries);
    }

    /// <summary>Signed Windows archives cannot silently become unsigned when requested from another OS.</summary>
    [Theory]
    [InlineData(BuildHost.Linux)]
    [InlineData(BuildHost.MacOS)]
    public void WindowsSigningRequiresWindowsHost(BuildHost host) =>
        Assert.Throws<ArgumentException>(() => InstallerPlan.Create("portable", "win-x64", "1.2.3", host, windowsSigningRequested: true));

    /// <summary>Native bundle metadata is numeric while portable archive version labels remain flexible.</summary>
    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2-beta.1")]
    [InlineData("1.2.3.4-beta.1")]
    public void MacBundleRejectsUnsupportedMetadataVersions(string version)
    {
        Assert.Throws<ArgumentException>(() => InstallerPlan.Create("native", "osx-arm64", version, BuildHost.MacOS));
        Assert.Equal(version, InstallerPlan.Create("portable", "osx-arm64", version, BuildHost.MacOS).Version);
    }

    /// <summary>Prerelease filenames remain explicit for every supported native target, including macOS.</summary>
    [Theory]
    [InlineData(BuildHost.Windows, "win-x64", "AthanTime-1.0.2-beta.1-Windows-x64-Setup.exe")]
    [InlineData(BuildHost.Linux, "linux-x64", "AthanTime-1.0.2-beta.1-Linux-x64.deb")]
    [InlineData(BuildHost.Linux, "linux-arm64", "AthanTime-1.0.2-beta.1-Linux-arm64.deb")]
    [InlineData(BuildHost.MacOS, "osx-x64", "AthanTime-1.0.2-beta.1-macOS-Intel-x64.dmg")]
    [InlineData(BuildHost.MacOS, "osx-arm64", "AthanTime-1.0.2-beta.1-macOS-AppleSilicon-arm64.dmg")]
    public void BetaPlansKeepFullReleaseVersion(BuildHost host, string runtime, string filename)
    {
        var plan = InstallerPlan.Create("native", runtime, "1.0.2-beta.1", host);
        Assert.Equal(filename, plan.FileName);
    }
}
