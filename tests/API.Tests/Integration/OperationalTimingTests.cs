using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace API.Tests.Integration;

public sealed class OperationalTimingTests
{
    private sealed class TimedFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder); builder.UseSetting("Performance:DiagnosticsEnabled", "true"); }
    }
    [Fact]
    public async Task TimingIsOperationalAndDoesNotAlterResponseBody()
    {
        using var plain = new TestWebApplicationFactory();
        using var timed = new TimedFactory();
        using var a = plain.CreateClient(); using var b = timed.CreateClient();
        using var expected = await a.GetAsync("/api/v1/health/live");
        using var actual = await b.GetAsync("/api/v1/health/live");
        Assert.Equal(await expected.Content.ReadAsStringAsync(), await actual.Content.ReadAsStringAsync());
        Assert.False(expected.Headers.Contains("Server-Timing"));
        Assert.Contains("headers;dur=", string.Join("", actual.Headers.GetValues("Server-Timing")));
    }
}
