using System.Text.Json;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class SettingCompilerTests
{
    [Fact]
    public async Task Compile_ResolvesDagVersionAndPatchInStableOrder()
    {
        var original = Json(new { damage = 5, tags = new[] { "attack" } });
        var provider = new FakeProvider(
            packages:
            [
                Package("addon", "1.0.0", dependencies: [Dependency("base", "^1.0.0")], content:
                [
                    Patch("patch.json", "boards/cards.json", "card", CanonicalJson.ComputeHash(original))
                ]),
                Package("base", "1.0.0", content: [Definition("old.json", "boards", "boards/cards.json")]),
                Package("base", "1.2.0", content: [Definition("cards.json", "boards", "boards/cards.json")])
            ],
            settings: [Setting("demo", "addon", "1.0.0")],
            payloads: new Dictionary<string, JsonElement>
            {
                ["base@1.0.0/old.json"] = Json(new { card = new { damage = 4, tags = new[] { "attack" } } }),
                ["base@1.2.0/cards.json"] = Json(new { card = original }),
                ["addon@1.0.0/patch.json"] = Json(new { damage = 8 })
            });

        var result = await new SettingCompiler([provider]).CompileAsync("demo");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Collection(
            result.Value.Packages,
            package => Assert.Equal(new CompiledPackageReference("base", "1.2.0"), package),
            package => Assert.Equal(new CompiledPackageReference("addon", "1.0.0"), package));
        var card = result.Value.Bundle.Artifacts["boards/cards.json"].GetProperty("card");
        Assert.Equal(8, card.GetProperty("damage").GetInt32());
        Assert.Contains(result.Value.Provenance, item =>
            item.PackageId == "addon" && item.JsonPointer == "/card/damage");
        Assert.Contains(result.Value.Provenance, item =>
            item.PackageId == "base" && item.JsonPointer == "/card/tags");
    }

    [Fact]
    public async Task Compile_IsIndependentOfProviderEnumerationAndRestart()
    {
        var packages = new[]
        {
            Package("z-addon", "1.0.0", dependencies: [Dependency("a-base")], content:
            [
                Definition("b.json", "boards", "boards/b.json")
            ]),
            Package("a-base", "1.0.0", content:
            [
                Definition("a.json", "boards", "boards/a.json")
            ])
        };
        var settings = new[] { Setting("stable", "z-addon") };
        var payloads = new Dictionary<string, JsonElement>
        {
            ["a-base@1.0.0/a.json"] = Json(new { a = new { value = 1 } }),
            ["z-addon@1.0.0/b.json"] = Json(new { b = new { value = 2 } })
        };

        var first = await new SettingCompiler([new FakeProvider(packages, settings, payloads)])
            .CompileAsync("stable");
        var restarted = await new SettingCompiler([new FakeProvider(
                packages.Reverse(),
                settings.Reverse(),
                payloads,
                reverseDiscovery: true)])
            .CompileAsync("stable");

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(restarted.IsSuccess, restarted.IsFailure ? restarted.Error : null);
        Assert.Equal(first.Value.Bundle.Manifest.Revision, restarted.Value.Bundle.Manifest.Revision);
        Assert.Equal(
            CanonicalJson.Serialize(first.Value.Bundle),
            CanonicalJson.Serialize(restarted.Value.Bundle));
    }

    [Theory]
    [InlineData("missing", "Required package is missing")]
    [InlineData("cycle", "dependency cycle")]
    [InlineData("conflict", "Package conflict")]
    [InlineData("version", "No compatible version")]
    public async Task Compile_RejectsInvalidDependencyGraphs(string scenario, string expected)
    {
        var packages = scenario switch
        {
            "missing" => new[] { Package("root", "1.0.0", dependencies: [Dependency("absent")]) },
            "cycle" => new[]
            {
                Package("root", "1.0.0", dependencies: [Dependency("other")]),
                Package("other", "1.0.0", dependencies: [Dependency("root")])
            },
            "conflict" => new[]
            {
                Package("root", "1.0.0", dependencies: [Dependency("other")], conflicts: [Conflict("other")]),
                Package("other", "1.0.0")
            },
            "version" => new[]
            {
                Package("root", "1.0.0", dependencies: [Dependency("other", ">=2.0.0")]),
                Package("other", "1.0.0")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = await new SettingCompiler([
                new FakeProvider(
                    packages,
                    [Setting("broken", "root")],
                    new Dictionary<string, JsonElement>())
            ])
            .CompileAsync("broken");

        Assert.True(result.IsFailure);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compile_RejectsDefinitionConflictAndPatchWithoutMatchingPrecondition()
    {
        var definitionConflict = new FakeProvider(
            packages:
            [
                Package("base", "1.0.0", content: [Definition("base.json", "boards", "boards/cards.json")]),
                Package("addon", "1.0.0", dependencies: [Dependency("base")], content:
                [
                    Definition("addon.json", "boards", "boards/cards.json")
                ])
            ],
            settings: [Setting("conflicting", "addon")],
            payloads: new Dictionary<string, JsonElement>
            {
                ["base@1.0.0/base.json"] = Json(new { card = new { damage = 1 } }),
                ["addon@1.0.0/addon.json"] = Json(new { card = new { damage = 2 } })
            });
        var invalidPatch = new FakeProvider(
            packages:
            [
                Package("base", "1.0.0", content: [Definition("base.json", "boards", "boards/cards.json")]),
                Package("addon", "1.0.0", dependencies: [Dependency("base")], content:
                [
                    Patch("patch.json", "boards/cards.json", "card", new string('0', 64))
                ])
            ],
            settings: [Setting("invalid-patch", "addon")],
            payloads: new Dictionary<string, JsonElement>
            {
                ["base@1.0.0/base.json"] = Json(new { card = new { damage = 1 } }),
                ["addon@1.0.0/patch.json"] = Json(new { damage = 2 })
            });

        var conflict = await new SettingCompiler([definitionConflict]).CompileAsync("conflicting");
        var patch = await new SettingCompiler([invalidPatch]).CompileAsync("invalid-patch");

        Assert.True(conflict.IsFailure);
        Assert.Contains("definition conflict", conflict.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(patch.IsFailure);
        Assert.Contains("precondition failed", patch.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Catalog_RejectsPackageIdCaseCollision()
    {
        var compiler = new SettingCompiler([
            new FakeProvider(
                [Package("Game", "1.0.0"), Package("game", "2.0.0")],
                [Setting("demo", "Game")],
                new Dictionary<string, JsonElement>())
        ]);

        var catalog = await compiler.GetCatalogAsync();
        var compilation = await compiler.CompileAsync("demo");

        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.Code == "PACKAGE_ID_CASE_COLLISION");
        Assert.True(compilation.IsFailure);
    }

    [Fact]
    public async Task DirectoryProvider_RejectsPathTraversal()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var provider = new DirectoryPackageProvider("test", root);
            var package = new PackageSource
            {
                ProviderId = "test",
                SourceId = ".",
                Manifest = Package("base", "1.0.0")
            };

            var result = await provider.ReadJsonAsync(package, "../outside.json");

            Assert.True(result.IsFailure);
            Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BaseGame_IsARebuildableValidPackageSetting()
    {
        var root = ResourceProviderFactory.FindProjectRoot();
        Assert.NotNull(root);
        var packageRoot = Path.Combine(root!, "data", "configs");

        var first = await new SettingCompiler([
                new DirectoryPackageProvider("base-game", packageRoot)
            ])
            .CompileAsync("default");
        var afterRestart = await new SettingCompiler([
                new DirectoryPackageProvider("base-game", packageRoot)
            ])
            .CompileAsync("default");

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(afterRestart.IsSuccess, afterRestart.IsFailure ? afterRestart.Error : null);
        Assert.Equal(first.Value.Bundle.Manifest.Revision, afterRestart.Value.Bundle.Manifest.Revision);
        var validation = new ContentGraphValidator().Validate(first.Value.Bundle);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }

    private static PackageManifest Package(
        string id,
        string version,
        IReadOnlyList<PackageDependency>? dependencies = null,
        IReadOnlyList<PackageConflict>? conflicts = null,
        IReadOnlyList<PackageContentEnvelope>? content = null) => new()
        {
            PackageId = id,
            Version = version,
            Dependencies = dependencies ?? [],
            Conflicts = conflicts ?? [],
            Content = content ?? []
        };

    private static PackageDependency Dependency(string id, string range = "*") => new()
    {
        PackageId = id,
        VersionRange = range
    };

    private static PackageConflict Conflict(string id, string range = "*") => new()
    {
        PackageId = id,
        VersionRange = range
    };

    private static PackageContentEnvelope Definition(string source, string kind, string artifact) => new()
    {
        Operation = PackageContentOperation.Definition,
        SourcePath = source,
        Kind = kind,
        ArtifactPath = artifact
    };

    private static PackageContentEnvelope Patch(
        string source,
        string artifact,
        string definitionId,
        string expectedHash) => new()
        {
            Operation = PackageContentOperation.Patch,
            SourcePath = source,
            ExpectedHash = expectedHash,
            Target = new PackagePatchTarget
            {
                PackageId = "base",
                Kind = "boards",
                ArtifactPath = artifact,
                DefinitionId = definitionId
            }
        };

    private static SettingSource Setting(string id, string packageId, string range = "*") => new()
    {
        ProviderId = "fake",
        PackageId = packageId,
        SourcePath = $"settings/{id}.json",
        Definition = new SettingDefinition
        {
            SettingId = id,
            Packages = [new SettingPackageReference { PackageId = packageId, VersionRange = range }]
        }
    };

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heroscript-packages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProvider : IPackageProvider
    {
        private readonly PackageSource[] _packages;
        private readonly SettingSource[] _settings;
        private readonly IReadOnlyDictionary<string, JsonElement> _payloads;
        private readonly bool _reverseDiscovery;

        public FakeProvider(
            IEnumerable<PackageManifest> packages,
            IEnumerable<SettingSource> settings,
            IReadOnlyDictionary<string, JsonElement> payloads,
            bool reverseDiscovery = false)
        {
            _packages = packages.Select(manifest => new PackageSource
            {
                ProviderId = ProviderId,
                SourceId = $"{manifest.PackageId}@{manifest.Version}",
                Manifest = manifest
            }).ToArray();
            _settings = settings.Select(setting => setting with { ProviderId = ProviderId }).ToArray();
            _payloads = payloads;
            _reverseDiscovery = reverseDiscovery;
        }

        public string ProviderId => "fake";

        public Task<PackageDiscovery> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PackageSource> packages = _reverseDiscovery ? _packages.Reverse().ToArray() : _packages;
            IReadOnlyList<SettingSource> settings = _reverseDiscovery ? _settings.Reverse().ToArray() : _settings;
            return Task.FromResult(new PackageDiscovery { Packages = packages, Settings = settings });
        }

        public Task<Result<JsonElement>> ReadJsonAsync(
            PackageSource package,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            var key = $"{package.SourceId}/{relativePath}";
            return Task.FromResult(_payloads.TryGetValue(key, out var payload)
                ? Result<JsonElement>.Success(payload.Clone())
                : Result<JsonElement>.Failure($"Missing fake payload: {key}"));
        }
    }
}
