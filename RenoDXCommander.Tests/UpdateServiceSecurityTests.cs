using System.Net;
using System.Security.Cryptography;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Phase 4 — automatic updates must never hand a launchable installer to the caller unless its
/// SHA-256 was verified. These exercise the download path with fixture bytes served by a fake
/// handler; no installer is ever executed.
/// </summary>
public sealed class UpdateServiceSecurityTests
{
    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        private readonly bool _understateLength;
        public int Calls { get; private set; }

        public FixtureHandler(byte[] body, bool understateLength = false)
        {
            _body = body;
            _understateLength = understateLength;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var content = new ByteArrayContent(_body);
            // Advertise more bytes than we send to simulate a truncated download.
            if (_understateLength) content.Headers.ContentLength = _body.Length + 16;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static (UpdateService svc, FixtureHandler handler) Make(byte[] body, bool understateLength = false)
    {
        var handler = new FixtureHandler(body, understateLength);
        var svc = new UpdateService(new HttpClient(handler), new GitHubETagCache());
        return (svc, handler);
    }

    private static readonly byte[] Fixture = System.Text.Encoding.ASCII.GetBytes("PRETEND-INSTALLER-BYTES");

    [Fact]
    public async Task MatchingHash_ReturnsVerifiedPath()
    {
        var (svc, _) = Make(Fixture);
        var path = await svc.DownloadInstallerAsync("https://example.test/Adas-Setup.exe", null, Sha256Hex(Fixture));

        Assert.False(string.IsNullOrEmpty(path));
        Assert.True(File.Exists(path));
        Assert.True(UpdateService.FileMatchesSha256(path!, Sha256Hex(Fixture)));
        try { File.Delete(path!); } catch { }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("abc123")] // too short
    public async Task MissingOrMalformedHash_NeverDownloadsAndReturnsNull(string? expected)
    {
        var (svc, handler) = Make(Fixture);
        var path = await svc.DownloadInstallerAsync("https://example.test/Adas-Setup.exe", null, expected);

        Assert.Null(path);
        Assert.Equal(0, handler.Calls); // failed closed before spending bandwidth
    }

    [Fact]
    public async Task MismatchedHash_DiscardsDownloadAndReturnsNull()
    {
        var (svc, _) = Make(Fixture);
        var wrong = Sha256Hex(System.Text.Encoding.ASCII.GetBytes("DIFFERENT"));
        var path = await svc.DownloadInstallerAsync("https://example.test/Adas-Setup.exe", null, wrong);

        Assert.Null(path);
        // No accepted installer left behind under the stable name.
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "Adas-Setup.exe"))
                     && UpdateService.FileMatchesSha256(Path.Combine(Path.GetTempPath(), "Adas-Setup.exe"), wrong));
    }

    [Fact]
    public async Task TruncatedDownload_ReturnsNull()
    {
        var (svc, _) = Make(Fixture, understateLength: true);
        var path = await svc.DownloadInstallerAsync("https://example.test/Adas-Setup.exe", null, Sha256Hex(Fixture));
        Assert.Null(path);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ABC", false)]
    [InlineData("0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789abcdef", true)]
    public void IsValidSha256_AcceptsOnly64Hex(string? value, bool expected)
        => Assert.Equal(expected, UpdateService.IsValidSha256(value));
}
