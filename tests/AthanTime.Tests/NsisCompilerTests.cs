using System.Net;
using InstallerBuilder;

namespace AthanTime.Tests;

/// <summary>Checks compiler discovery and download rejection without network access or running native tools.</summary>
public sealed class NsisCompilerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("athan-nsis-tests-").FullName;

    /// <summary>Installed compilers work without creating a portable cache or making a request.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledCompilerAvoidsDownloadsAndCacheWrites(bool binLayout)
    {
        var installed = Path.Combine(_directory, "installed with spaces", binLayout ? "Bin" : "", "makensis.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "Test sentinel; never executed.");
        using var client = new HttpClient(new FakeDownload());
        var cache = Path.Combine(_directory, "cache");
        var result = await new NsisCompiler(client).ResolveAsync(cache,
            [Path.Combine(_directory, "missing.exe"), installed], TestContext.Current.CancellationToken);
        Assert.Equal(installed, result);
        Assert.False(Directory.Exists(cache));
    }

    /// <summary>A cached compiler never bypasses verification of its locally provisioned archive.</summary>
    [Fact]
    public async Task InvalidCachedArchiveIsRejectedBeforeUsingTheCompiler()
    {
        var cache = Path.Combine(_directory, "cache");
        var compiler = Path.Combine(cache, $"nsis-{NsisCompiler.Version}", "Bin", "makensis.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(compiler)!);
        File.WriteAllText(compiler, "Untrusted compiler sentinel.");
        File.WriteAllText(Path.Combine(cache, $"nsis-{NsisCompiler.Version}.zip"), "Not an official NSIS archive.");
        using var client = new HttpClient(new FakeDownload());
        var error = await Assert.ThrowsAsync<IOException>(() => new NsisCompiler(client).ResolveAsync(
            cache, [], TestContext.Current.CancellationToken));
        Assert.Contains("checksum mismatch", error.Message);
        Assert.Equal("Untrusted compiler sentinel.", File.ReadAllText(compiler));
    }

    /// <summary>Unverified downloads are removed and never extracted or executed.</summary>
    [Fact]
    public async Task DownloadWithWrongChecksumLeavesNoUsableCache()
    {
        var cache = Path.Combine(_directory, "cache");
        var download = new FakeDownload(HttpStatusCode.OK);
        using var client = new HttpClient(download);
        var error = await Assert.ThrowsAsync<IOException>(() => new NsisCompiler(client).ResolveAsync(
            cache, [], TestContext.Current.CancellationToken));
        Assert.Contains("checksum mismatch", error.Message);
        Assert.Equal(NsisCompiler.DownloadUrl, Assert.Single(download.Requests));
        Assert.Empty(Directory.GetFileSystemEntries(cache));
    }

    /// <summary>HTTP failures leave no partial archive and explain the manual/offline recovery path.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FailedDownloadExplainsRecoveryAndLeavesNoPartialArchive(HttpStatusCode status)
    {
        var cache = Path.Combine(_directory, "cache");
        var download = new FakeDownload(status);
        using var client = new HttpClient(download);
        var error = await Assert.ThrowsAsync<IOException>(() => new NsisCompiler(client).ResolveAsync(
            cache, [], TestContext.Current.CancellationToken));
        Assert.Contains("Cannot download portable NSIS", error.Message);
        Assert.Contains("install NSIS 3", error.Message);
        Assert.Single(download.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(cache));
    }

    /// <summary>Transport timeouts are reported as actionable packaging failures.</summary>
    [Fact]
    public async Task DownloadTimeoutExplainsRecoveryAndLeavesNoPartialArchive()
    {
        var cache = Path.Combine(_directory, "timeout-cache");
        using var client = new HttpClient(new TimeoutDownload());
        var error = await Assert.ThrowsAsync<IOException>(() => new NsisCompiler(client).ResolveAsync(
            cache, [], TestContext.Current.CancellationToken));
        Assert.Contains("download timed out", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(cache));
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FakeDownload(HttpStatusCode? status = null) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.NotNull(status); // Discovery/cache-only tests must never request a download.
            return Task.FromResult(new HttpResponseMessage(status.Value)
                { Content = new StringContent("Invalid archive bytes, served entirely in memory.") });
        }
    }

    private sealed class TimeoutDownload : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("Simulated transport timeout.");
    }
}
