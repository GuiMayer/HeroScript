using API.Models;
using Core.Run;

namespace API.Services;

public static class ToolCapabilities
{
    public const string TimelineRead = "timeline.read";
    public const string TimelineInspectState = "timeline.inspect_state";
    public const string ReplayVerify = "replay.verify";
    public const string BranchRead = "timeline.branch.read";
    public const string BranchCreate = "timeline.branch.create";
    public const string HeadRestore = "timeline.restore_head";
    public const string SimulationRun = "simulation.run";
    public const string ScenarioAuthor = "scenario.author";
    public const string CheatRunResources = "cheat.run_resources";
    public const string CheatCardZones = "cheat.card_zones";
    public const string CardInspectFull = "debug.card_inspection.full";
    public const string ContentActivate = "content.activate";
    public const string AdminOperations = "admin.operations";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TimelineRead,
        TimelineInspectState,
        ReplayVerify,
        BranchRead,
        BranchCreate,
        HeadRestore,
        SimulationRun,
        ScenarioAuthor,
        CheatRunResources,
        CheatCardZones,
        CardInspectFull,
        ContentActivate,
        AdminOperations
    };
}

public sealed record ToolAccessSnapshot(
    string Profile,
    IReadOnlyList<string> GrantedCapabilities);

public interface IToolAccessPolicy
{
    ToolAccessSnapshot Snapshot { get; }
    bool Allows(string capability);
    bool Allows(RunState run, string capability);
    IReadOnlyList<string> EffectiveFor(RunState run);
}

/// <summary>
/// Intersects the trusted operator profile with the immutable ceiling captured
/// by a run's game mode. It authorizes tools; gameplay legality stays in Core.
/// </summary>
public sealed class ToolAccessPolicy : IToolAccessPolicy
{
    private readonly HashSet<string> _granted;

    public ToolAccessPolicy(ToolAccessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var profile = NormalizeProfile(settings.Profile);
        _granted = ResolveProfile(profile, settings.CustomCapabilities);
        Snapshot = new ToolAccessSnapshot(profile, _granted.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    public ToolAccessSnapshot Snapshot { get; }

    public bool Allows(string capability) => _granted.Contains(capability);

    public bool Allows(RunState run, string capability) =>
        Allows(capability) && ModeAllows(run, capability);

    public IReadOnlyList<string> EffectiveFor(RunState run) => _granted
        .Where(capability => ModeAllows(run, capability))
        .OrderBy(capability => capability, StringComparer.Ordinal)
        .ToArray();

    private static bool ModeAllows(RunState run, string capability)
    {
        var mode = run.ResolvedMode;
        if (mode == null)
            return false;
        var timelineVisible = mode.TimelinePolicy.Enabled &&
            mode.ReplayPolicy.TimelineAccess != TimelineAccessLevel.None;
        return capability switch
        {
            ToolCapabilities.TimelineRead => timelineVisible,
            ToolCapabilities.TimelineInspectState => timelineVisible && mode.ReplayPolicy.AllowHistoricalInspection,
            ToolCapabilities.ReplayVerify => mode.ReplayPolicy.SemanticVerification,
            ToolCapabilities.BranchRead or ToolCapabilities.BranchCreate =>
                mode.CapabilityPolicy.AllowTimelineFork && mode.ReplayPolicy.AllowForkFromHistory,
            ToolCapabilities.HeadRestore => mode.ReplayPolicy.AllowHeadRestore,
            ToolCapabilities.SimulationRun => mode.CapabilityPolicy.AllowCombatSimulation,
            ToolCapabilities.ScenarioAuthor => mode.CapabilityPolicy.AllowScenarioAuthoring,
            ToolCapabilities.CheatRunResources => mode.CapabilityPolicy.AllowRunResourceCheats,
            ToolCapabilities.CheatCardZones => mode.CapabilityPolicy.AllowCardZoneCheats,
            ToolCapabilities.CardInspectFull =>
                mode.CapabilityPolicy.CardInspectionDetail == InspectionDetailLevel.Full,
            ToolCapabilities.ContentActivate => mode.CapabilityPolicy.AllowHotReloadActivation,
            ToolCapabilities.AdminOperations => true,
            _ => false
        };
    }

    private static string NormalizeProfile(string? profile)
    {
        var normalized = profile?.Trim().ToLowerInvariant().Replace('-', '_') ?? string.Empty;
        return normalized switch
        {
            ToolAccessProfiles.Normal or
            ToolAccessProfiles.Experimental or
            ToolAccessProfiles.Sandbox or
            ToolAccessProfiles.DevModder or
            ToolAccessProfiles.Custom => normalized,
            _ => throw new InvalidOperationException($"Unknown tool access profile: {profile}")
        };
    }

    private static HashSet<string> ResolveProfile(string profile, IEnumerable<string>? custom)
    {
        var normal = new[]
        {
            ToolCapabilities.TimelineRead,
            ToolCapabilities.TimelineInspectState,
            ToolCapabilities.ReplayVerify
        };
        var experimental = normal.Concat(new[]
        {
            ToolCapabilities.CheatRunResources,
            ToolCapabilities.CheatCardZones
        });
        var sandbox = experimental.Concat(new[]
        {
            ToolCapabilities.BranchRead,
            ToolCapabilities.BranchCreate,
            ToolCapabilities.SimulationRun,
            ToolCapabilities.ScenarioAuthor,
            ToolCapabilities.CardInspectFull
        });
        var capabilities = profile switch
        {
            ToolAccessProfiles.Normal => normal,
            ToolAccessProfiles.Experimental => experimental,
            ToolAccessProfiles.Sandbox => sandbox,
            ToolAccessProfiles.DevModder => ToolCapabilities.All,
            ToolAccessProfiles.Custom => custom ?? [],
            _ => []
        };
        var result = capabilities
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var unknown = result.Where(item => !ToolCapabilities.All.Contains(item)).OrderBy(item => item).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Unknown custom tool capabilities: {string.Join(", ", unknown)}");
        if (result.Contains(ToolCapabilities.BranchCreate))
            result.Add(ToolCapabilities.BranchRead);
        if (result.Contains(ToolCapabilities.TimelineInspectState))
            result.Add(ToolCapabilities.TimelineRead);
        return result;
    }
}
