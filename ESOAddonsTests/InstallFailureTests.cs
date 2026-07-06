using System.IO;
using System.Net;
using System.Net.Http;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class InstallFailureTests
{
    [Fact]
    public void Classify_403_as_cdn_forbidden()
        => Assert.Equal(InstallFailure.Kind.CdnForbidden,
            InstallFailure.Classify(new HttpRequestException("403", null, HttpStatusCode.Forbidden)));

    [Fact]
    public void Classify_connection_and_timeout_as_network()
    {
        Assert.Equal(InstallFailure.Kind.Network, InstallFailure.Classify(new HttpRequestException("no route")));
        Assert.Equal(InstallFailure.Kind.Network, InstallFailure.Classify(new TaskCanceledException()));
    }

    [Fact]
    public void Classify_filesystem_failures_as_write_blocked()
    {
        // The three shapes seen for blocked AddOns writes: CFA/AV denial, a Win32 FILE_NOT_FOUND
        // surfaced mid-extraction (field report: '...\SkyShards\console'), and a dead junction.
        Assert.Equal(InstallFailure.Kind.WriteBlocked, InstallFailure.Classify(new UnauthorizedAccessException()));
        Assert.Equal(InstallFailure.Kind.WriteBlocked, InstallFailure.Classify(new FileNotFoundException()));
        Assert.Equal(InstallFailure.Kind.WriteBlocked, InstallFailure.Classify(new DirectoryNotFoundException()));
        Assert.Equal(InstallFailure.Kind.WriteBlocked, InstallFailure.Classify(new IOException()));
    }

    [Fact]
    public void Classify_everything_else_as_other()
        => Assert.Equal(InstallFailure.Kind.Other, InstallFailure.Classify(new InvalidOperationException()));

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true)]           // Cloudflare rate-limit ban decays
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.NotFound, false)]           // permanent — retrying is pointless
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void IsTransient_by_status_code(HttpStatusCode code, bool expected)
        => Assert.Equal(expected, EsouiClient.IsTransient(new HttpRequestException("x", null, code)));

    [Fact]
    public void IsTransient_connection_failures_and_timeouts()
    {
        Assert.True(EsouiClient.IsTransient(new HttpRequestException("dns")));   // no status = connection-level
        Assert.True(EsouiClient.IsTransient(new TaskCanceledException()));       // HttpClient timeout
        Assert.False(EsouiClient.IsTransient(new InvalidOperationException()));
    }

    [Fact]
    public void ProbeWrite_ok_on_writable_folder_and_leaves_no_trace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shoyru-probe-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(InstallFailure.ProbeWrite(dir));
            Assert.Empty(Directory.GetFileSystemEntries(dir));   // probe cleans up after itself
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ProbeWrite_returns_exception_when_folder_cannot_be_created()
    {
        // A FILE where the folder should be — CreateDirectory must fail, and the probe reports it.
        var file = Path.Combine(Path.GetTempPath(), "shoyru-probe-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try { Assert.NotNull(InstallFailure.ProbeWrite(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UserAgent_is_versioned()
        => Assert.Matches(@"^ShoyruAddonSuite/\d+\.\d+\.\d+$", EsouiClient.UserAgent);
}
