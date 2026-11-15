using System.Net;
using API.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace API.Tests.Authoring;

public sealed class EditorLaunchSessionTests
{
    private const string Code = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string Origin = "http://127.0.0.1:5279";

    [Fact]
    public void ExchangesAValidCodeOnceAndNeverCachesTheResponse()
    {
        var session = new EditorLaunchSession(Code, "local-test-key");
        var context = Request(IPAddress.Loopback, Origin);

        Assert.Equal(401, Status(session.Exchange(context, new(new string('A', 64)))));
        var issued = Assert.IsAssignableFrom<IValueHttpResult>(session.Exchange(context, new(Code)));
        Assert.Equal("local-test-key", issued.Value!.GetType().GetProperty("key")!.GetValue(issued.Value));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(401, Status(session.Exchange(context, new(Code))));
    }

    [Fact]
    public void RejectsRequestsFromOutsideTheLauncherOriginWithoutConsumingCode()
    {
        var session = new EditorLaunchSession(Code, "local-test-key");

        Assert.Equal(403, Status(session.Exchange(Request(IPAddress.Parse("192.0.2.8"), Origin), new(Code))));
        Assert.Equal(403, Status(session.Exchange(Request(IPAddress.Loopback, "http://127.0.0.1:8080"), new(Code))));
        Assert.Equal(403, Status(session.Exchange(Request(IPAddress.Loopback, "http://other.example:5279", "other.example"), new(Code))));
        Assert.IsAssignableFrom<IValueHttpResult>(session.Exchange(Request(IPAddress.Loopback, Origin), new(Code)));
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static DefaultHttpContext Request(IPAddress address, string origin, string host = "127.0.0.1")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(host, 5279);
        context.Request.Headers.Origin = origin;
        return context;
    }
}
