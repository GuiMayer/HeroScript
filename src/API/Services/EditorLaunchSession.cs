using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace API.Services;

/// <summary>Exchanges one short-lived launcher code for the local editor's session key.</summary>
public sealed class EditorLaunchSession(string code, string adminKey)
{
    private readonly byte[] _digest = SHA256.HashData(Encoding.UTF8.GetBytes(code));
    private readonly long _created = System.Diagnostics.Stopwatch.GetTimestamp();
    private int _used;

    public IResult Exchange(HttpContext context, EditorLaunchRequest launch)
    {
        context.Response.Headers.CacheControl = "no-store";
        var request = context.Request;
        if (context.Connection.RemoteIpAddress is not { } client || !IPAddress.IsLoopback(client) ||
            request.Host.Host != "127.0.0.1" ||
            !string.Equals(request.Headers.Origin, $"{request.Scheme}://{request.Host}", StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (launch.Code is not { Length: 64 } ||
            System.Diagnostics.Stopwatch.GetElapsedTime(_created) > TimeSpan.FromMinutes(5) ||
            !CryptographicOperations.FixedTimeEquals(_digest, SHA256.HashData(Encoding.UTF8.GetBytes(launch.Code))) ||
            Interlocked.Exchange(ref _used, 1) != 0)
            return Results.Unauthorized();

        return Results.Json(new { key = adminKey });
    }
}

public sealed record EditorLaunchRequest(string? Code);
