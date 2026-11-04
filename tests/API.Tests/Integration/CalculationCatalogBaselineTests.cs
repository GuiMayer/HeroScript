using System.Net.Http.Json;
using System.Text.Json;
using Core.Calculations;
using Core.Content;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class CalculationCatalogBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PublishedModesResolveTheirEnabledPipelinesAndDefaultsAtPinnedRevision()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var resolver = factory.Services.GetRequiredService<IContentRuntimeResolver>();
        var count = 0;
        foreach (var setting in catalog.GetProperty("items").EnumerateArray())
        {
            var id = setting.GetProperty("settingId").GetString()!;
            var revision = setting.GetProperty("currentRevision").GetString()!;
            var runtime = resolver.Resolve(revision, id);
            Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
            foreach (var modeId in runtime.Value.GetDefinitions("modes").Keys.Order(StringComparer.Ordinal))
            {
                var mode = runtime.Value.GetDefinition<GameModeDefinition>("modes", modeId);
                Assert.True(mode.IsSuccess, mode.IsFailure ? mode.Error : null);
                foreach (var pipelineId in mode.Value.CalculationPipelineIds)
                {
                    var pipeline = runtime.Value.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", pipelineId);
                    Assert.True(pipeline.IsSuccess, pipeline.IsFailure ? pipeline.Error : null);
                    var valid = CalculationEngine.ValidateDefinition(pipeline.Value);
                    Assert.True(valid.IsSuccess, valid.IsFailure ? valid.Error : null);
                    count++;
                }
                foreach (var pair in mode.Value.DefaultCalculationPipelines)
                {
                    Assert.Contains(pair.Value, mode.Value.CalculationPipelineIds);
                    Assert.Equal(pair.Key, runtime.Value.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", pair.Value).Value.Channel);
                }
            }
            output.WriteLine($"SETTING_BASELINE setting={id}; revision={revision}");
        }
        Assert.True(count > 0);
        output.WriteLine($"PIPELINE_BINDINGS_VALIDATED {count}");
    }
}
