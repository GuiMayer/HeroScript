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

    [Fact]
    public async Task ResolverAndCapturePreviewPreserveRunAndRepeatFingerprints()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new API.Tests.Helpers.GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8815, "prototype_campaign");
        var run = factory.Services.GetRequiredService<IRunQueryService>().GetRun(id).Value;
        var before = Core.Determinism.CanonicalJson.ComputeHash(run);
        var resolver = new CalculationResolver(factory.Services.GetRequiredService<Core.Math.IRuntimeFormulaEvaluator>(), factory.Services.GetRequiredService<IContentRuntimeResolver>(), factory.Services.GetRequiredService<ICalculationEngine>(), factory.Services.GetRequiredService<ICalculationInfluenceProvider>());
        var effect = new Core.Effects.EffectDefinition { EffectId = "baseline", Type = Core.Effects.EffectType.MODIFY_RESOURCE,
            FlatValue = 10, CalculationChannel = "resource_delta", CalculationPipelineId = "signed_resource_delta" };
        foreach (var capture in new[] { false, true })
        {
            var context = new CalculationSourceContext { Run = run, ContentRevision = run.Determinism.ContentRevision, CaptureOnly = capture };
            var reference = resolver.Resolve(effect, "baseline", context);
            Assert.True(reference.IsSuccess, reference.IsFailure ? reference.Error : null);
            var samples = new List<double>();
            for (var n = 0; n < 120; n++)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var result = resolver.Resolve(effect, "baseline", context);
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
                Assert.Equal(reference.Value.Calculation!.Fingerprint, result.Value.Calculation!.Fingerprint);
                if (n >= 20) samples.Add(elapsed);
            }
            samples.Sort();
            output.WriteLine($"RESOLVER_BASELINE capture={capture}; n=100; warmup=20; p50={samples[49]:F4}; p95={samples[94]:F4}; p99={samples[98]:F4}; fingerprint={reference.Value.Calculation!.Fingerprint}");
        }
        Assert.Equal(before, Core.Determinism.CanonicalJson.ComputeHash(run));
    }
}
