using Core.Config;
using Core.Content.Migrations;
using Core.Determinism;
using Mods;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class EffectiveBundleResponseMigrationTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("ascendant")]
    [InlineData("volatile-core")]
    [InlineData("compartment-lab")]
    [InlineData("uno-clash")]
    [InlineData("circular-table")]
    public async Task EffectivePackagePlanIsRepeatableValidatedAndNeverChangesCapturedContent(string setting)
    {
        var root = ResourceProviderFactory.FindProjectRoot(); Assert.NotNull(root);
        var compiler = new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(root!, "data", "configs"))]);
        var compiled = await compiler.CompileBundleAsync(setting);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        var before = CanonicalJson.ComputeHash(compiled.Value);
        var hashes = new HashSet<string>();
        for (var i = 0; i < 3; i++)
        {
            var plan = GameplayContentResponseMigration.Plan(compiled.Value);
            Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error : null);
            Assert.True(plan.Value.Validation.IsValid, string.Join("; ", plan.Value.Validation.Errors));
            Assert.Equal(setting, plan.Value.Candidate.Manifest.ConfigName);
            if (!plan.Value.Changes.IsEmpty) Assert.NotEqual(compiled.Value.Manifest.Revision, plan.Value.Candidate.Manifest.Revision);
            var next = GameplayContentResponseMigration.Plan(plan.Value.Candidate);
            Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
            Assert.Empty(next.Value.Changes);
            Assert.Same(plan.Value.Candidate, next.Value.Candidate);
            hashes.Add(CanonicalJson.ComputeHash(plan.Value));
        }
        Assert.Single(hashes); Assert.Equal(before, CanonicalJson.ComputeHash(compiled.Value));
        var bad = compiled.Value with { Manifest = compiled.Value.Manifest with { Revision = new string('0', 64) } };
        Assert.True(GameplayContentResponseMigration.Plan(bad).IsFailure);
    }
}
