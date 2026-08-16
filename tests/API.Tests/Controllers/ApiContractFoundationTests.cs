using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Contracts;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class ApiContractFoundationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiContractFoundationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/health/live")]
    [InlineData("/api/v1/health/ready")]
    [InlineData("/api/v1/version")]
    [InlineData("/api/v1/capabilities")]
    public async Task VersionedSystemEndpoints_AreAvailable(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task InvalidModel_ReturnsProblemDetailsWithCorrelationId()
    {
        const string correlationId = "contract-test-correlation";
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/status/remove")
        {
            Content = JsonContent.Create(new { targetId = "enemy_1", instanceId = "not-a-guid" })
        };
        request.Headers.Add("X-Correlation-ID", correlationId);

        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("INVALID_REQUEST", json.GetProperty("code").GetString());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task ContentRevisionEndpoints_ExposeCanonicalManifest()
    {
        using var listResponse = await _client.GetAsync("/api/v1/content/revisions?configName=default");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        Assert.True(listResponse.StatusCode == HttpStatusCode.OK, listBody);
        var list = JsonSerializer.Deserialize<JsonElement>(listBody);
        var revision = list.GetProperty("currentRevision").GetString();
        Assert.NotNull(revision);
        Assert.Equal(64, revision.Length);
        Assert.NotEmpty(list.GetProperty("revisions").EnumerateArray());

        using var manifestResponse = await _client.GetAsync($"/api/v1/content/revisions/{revision}");
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        Assert.Equal(revision, manifest.GetProperty("revision").GetString());
        Assert.NotEmpty(manifest.GetProperty("artifacts").EnumerateArray());
    }

    [Fact]
    public void CommandEnvelope_RejectsMissingIdentityAndType()
    {
        var missingIdentity = new CommandEnvelope { Type = "TEST" };
        var identityResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var identityValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            missingIdentity,
            new System.ComponentModel.DataAnnotations.ValidationContext(missingIdentity),
            identityResults,
            validateAllProperties: true);

        var missingType = new CommandEnvelope { CommandId = Guid.NewGuid() };
        var typeResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var typeValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            missingType,
            new System.ComponentModel.DataAnnotations.ValidationContext(missingType),
            typeResults,
            validateAllProperties: true);

        Assert.False(identityValid);
        Assert.Contains(identityResults, result => result.MemberNames.Contains(nameof(CommandEnvelope.CommandId)));
        Assert.False(typeValid);
        Assert.Contains(typeResults, result => result.MemberNames.Contains(nameof(CommandEnvelope.Type)));
    }
}
