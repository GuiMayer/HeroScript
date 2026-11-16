namespace API.Contracts;

public static class CompositionAuthoringKinds
{
    public const string Settings = "settings";
    public const string Packages = "packages";
    public const string PackageSources = "package-sources";
    public const string PackageIds = "package-ids";
    internal const string Scope = "";

    public static bool Supports(string kind) => kind is Settings or Packages;

    public static string? Identity(string kind) => kind == Settings ? "settingId" : null;

    public static IReadOnlyDictionary<string, string> References(string kind) => kind == Settings
        ? new Dictionary<string, string>
        {
            ["/packages/*/packageId"] = PackageIds, ["/launch/runDefinitionId"] = "runs",
            ["/launch/playerEntityId"] = "entities", ["/launch/modeId"] = "modes"
        }
        : new Dictionary<string, string>
        {
            ["/dependencies/*/packageId"] = PackageIds, ["/conflicts/*/packageId"] = PackageIds,
            ["/content/*/kind"] = "content-kinds", ["/content/*/target/kind"] = "content-kinds",
            ["/content/*/target/packageId"] = PackageIds
        };
}
