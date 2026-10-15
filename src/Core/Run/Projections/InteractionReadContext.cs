using System.Text.Json.Serialization;
using Core.Determinism;

namespace Core.Run.Projections;

public sealed record InteractionVersion(Guid RunId, string SettingId, int RunSequence,
    ulong RunStep, Guid? CombatId, ulong? CombatStep, string ContentRevision,
    string EngineVersion, string StateFingerprint);

/// <summary>One immutable aggregate reference per read. No reloads or gameplay execution.</summary>
public sealed record InteractionReadContext
{
    [JsonIgnore] public RunState Run { get; }
    public InteractionVersion Version { get; }
    public InteractionReadContext(RunState run)
    {
        Run = run;
        Version = new(run.RunId, run.SettingId, run.Sequence, run.Determinism.Step,
            run.ActiveEncounterId, run.GetActiveEncounter()?.Combat.Determinism.Step,
            run.Determinism.ContentRevision, run.Determinism.EngineVersion, CanonicalJson.ComputeHash(run));
    }
}
