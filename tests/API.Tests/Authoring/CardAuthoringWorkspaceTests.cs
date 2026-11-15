using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Content;
using Microsoft.AspNetCore.Hosting;
using Mods;
using Xunit;

namespace API.Tests.Authoring;

public sealed class CardAuthoringWorkspaceTests(CardAuthoringWorkspaceTests.WorkspaceFactory factory)
    : IClassFixture<CardAuthoringWorkspaceTests.WorkspaceFactory>
{
    [Theory]
    [InlineData("uno-clash", "uno_red_0", "heroscript.uno-clash", "Resources/cards/uno.json", "circular-table", "t_red_0")]
    [InlineData("circular-table", "t_red_0", "heroscript.circular-table", "Resources/cards/table.json", "uno-clash", "uno_red_0")]
    public async Task RealSettingsExposeSeparatePublishedCardsAndSourceProvenance(
        string setting, string card, string package, string source, string otherSetting, string otherCard)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/settings/{setting}");
        request.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var compilation = await response.Content.ReadFromJsonAsync<SettingCompilation>();
        Assert.NotNull(compilation);
        Assert.Contains(compilation.Provenance, entry =>
            entry.Kind == "cards" && entry.DefinitionId == card && entry.PackageId == package &&
            entry.SourcePath == source && entry.Operation == PackageContentOperation.Definition);

        var revision = compilation.Bundle.Manifest.Revision;
        var definition = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/content/cards/{card}?configName={setting}&revision={revision}");
        Assert.Equal(card, definition.GetProperty("definition").GetProperty("cardId").GetString());

        using var wrongCard = await client.GetAsync($"/api/v1/content/cards/{otherCard}?configName={setting}&revision={revision}");
        using var wrongSetting = await client.GetAsync($"/api/v1/content/cards/{card}?configName={otherSetting}&revision={revision}");
        Assert.Equal(HttpStatusCode.NotFound, wrongCard.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongSetting.StatusCode);
    }

    [Fact]
    public async Task SourceVersionDetectsByteChangesEvenWhenCompilationDoesNot()
    {
        var root = Path.Combine(factory.PersistenceRoot, "source-version");
        WorkspaceFactory.CopyPackages(root);
        var compiler = new SettingCompiler([new DirectoryPackageProvider("version-test", root)]);
        var firstCompilation = await compiler.CompileAsync("uno-clash");
        Assert.True(firstCompilation.IsSuccess, firstCompilation.IsFailure ? firstCompilation.Error : null);

        var first = await ReadSnapshot(root);
        var version = AuthoringSourceVersion.CreateETag(first);
        Assert.Matches("^\"[0-9a-f]{64}\"$", version);
        Assert.Equal(version, AuthoringSourceVersion.CreateETag(first.Reverse().ToDictionary()));

        var path = Path.Combine(root, "uno-clash", "Resources", "cards", "uno.json");
        await File.AppendAllTextAsync(path, "\n");
        var secondCompilation = await compiler.CompileAsync("uno-clash");
        Assert.True(secondCompilation.IsSuccess, secondCompilation.IsFailure ? secondCompilation.Error : null);
        Assert.Equal(firstCompilation.Value.Bundle.Manifest.Revision, secondCompilation.Value.Bundle.Manifest.Revision);
        Assert.NotEqual(version, AuthoringSourceVersion.CreateETag(await ReadSnapshot(root)));
    }

    [Fact]
    public async Task SharedPackageFixtureExposesInheritedCardsAndBlocksDeletingAReferencedCard()
    {
        var root = Path.Combine(factory.PersistenceRoot, "shared-fixture");
        var shared = Path.Combine(root, "base");
        Directory.CreateDirectory(shared);
        await File.WriteAllTextAsync(Path.Combine(shared, "package.json"), """
            {
              "packageId": "fixture.base",
              "version": "1.0.0",
              "content": [
                { "operation": "definition", "kind": "cards", "sourcePath": "cards.json", "artifactPath": "cards/shared.json" },
                { "operation": "definition", "kind": "card-pools", "sourcePath": "pools.json", "artifactPath": "card-pools/shared.json" }
              ]
            }
            """);
        var cardsPath = Path.Combine(shared, "cards.json");
        const string retained = """
            {"cardId":"retained","tags":["existing"],"components":[{"type":"condition","componentId":"ready","expression":"1"}]}
            """;
        await File.WriteAllTextAsync(cardsPath, $$"""
            {
              "retained": {{retained}},
              "referenced": {"cardId":"referenced","components":[{"type":"condition","componentId":"ready","expression":"1"}]}
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(shared, "pools.json"),
            """{"shared":{"poolId":"shared","explicitCardIds":["referenced"]}}""");

        foreach (var setting in new[] { "fixture-a", "fixture-b" })
        {
            var package = Path.Combine(root, setting);
            var settings = Directory.CreateDirectory(Path.Combine(package, "settings"));
            await File.WriteAllTextAsync(Path.Combine(package, "package.json"), $$"""
                {
                  "packageId": "{{setting}}",
                  "version": "1.0.0",
                  "dependencies": [{ "packageId": "fixture.base", "versionRange": "1.0.0" }]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(settings.FullName, setting + ".json"), $$"""
                { "settingId": "{{setting}}", "packages": [{ "packageId": "{{setting}}", "versionRange": "1.0.0" }] }
                """);
        }

        var compiler = new SettingCompiler([new DirectoryPackageProvider("shared-fixture", root)]);
        var validator = new ContentGraphValidator();
        foreach (var setting in new[] { "fixture-a", "fixture-b" })
        {
            var compiled = await compiler.CompileAsync(setting);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
            Assert.Contains(compiled.Value.Provenance, item =>
                item.DefinitionId == "referenced" && item.Kind == "cards" && item.PackageId == "fixture.base");
            var validation = validator.Validate(compiled.Value.Bundle);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        }

        await File.WriteAllTextAsync(cardsPath, $$"""{"retained":{{retained}} }""");
        foreach (var setting in new[] { "fixture-a", "fixture-b" })
        {
            var compiled = await compiler.CompileAsync(setting);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
            var validation = validator.Validate(compiled.Value.Bundle);
            Assert.Contains(validation.Errors, message =>
                message.Contains("card-pools/shared", StringComparison.Ordinal) &&
                message.Contains("referenced", StringComparison.Ordinal));
        }
    }

    private static async Task<Dictionary<string, ReadOnlyMemory<byte>>> ReadSnapshot(string root)
    {
        var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), await File.ReadAllBytesAsync(path));
        return files;
    }

    public sealed class WorkspaceFactory : TestWebApplicationFactory
    {
        private readonly string _packageRoot;

        public WorkspaceFactory()
        {
            _packageRoot = Path.Combine(PersistenceRoot, "packages");
            CopyPackages(_packageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Mods:PackageRoots:0", _packageRoot);
            builder.UseSetting("Content:StartupSettings:0", "uno-clash");
            builder.UseSetting("Content:StartupSettings:1", "circular-table");
        }

        internal static void CopyPackages(string destination)
        {
            var repository = FindProjectRoot() ?? throw new InvalidOperationException("HeroScript checkout not found.");
            foreach (var package in new[] { "default", "uno-clash", "circular-table" })
            {
                var source = Path.Combine(repository, "data", "configs", package);
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(destination, package, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
        }
    }
}
