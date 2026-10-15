using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Determinism;

[Trait("Category", "Unit")]
public class CanonicalJsonTests
{
    [Fact]
    public void Serialize_OrdersObjectAndDictionaryPropertiesOrdinally()
    {
        var value = new
        {
            Zebra = 1,
            Attributes = new Dictionary<string, int>
            {
                ["speed"] = 3,
                ["armor"] = 8
            }
        };

        var json = CanonicalJson.Serialize(value);

        Assert.Equal("{\"attributes\":{\"armor\":8,\"speed\":3},\"zebra\":1}", json);
    }

    [Fact]
    public void ComputeHash_IsIndependentOfDictionaryInsertionOrder()
    {
        var first = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };
        var second = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(second));
    }

    [Fact]
    public void ComputeHash_PreservesArrayOrderAsDomainMeaning()
    {
        Assert.NotEqual(
            CanonicalJson.ComputeHash(new[] { "a", "b" }),
            CanonicalJson.ComputeHash(new[] { "b", "a" }));
    }

    [Fact]
    public void HashScope_ReusesOnlyTheSameOptedInImmutableInstance()
    {
        var converter = new CountingConverter();
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        var value = new MemoizableValue(7);

        string first;
        using (CanonicalJson.BeginHashScope())
        {
            first = CanonicalJson.ComputeHash(value, options);
            Assert.Equal(first, CanonicalJson.ComputeHash(value, options));
            Assert.Equal(1, converter.Writes);
            Assert.NotEqual(first, CanonicalJson.ComputeHash(new MemoizableValue(8), options));
            Assert.Equal(2, converter.Writes);
        }

        Assert.Equal(first, CanonicalJson.ComputeHash(value, options));
        Assert.Equal(3, converter.Writes);
    }

    private sealed record MemoizableValue(int Value) : ICanonicalHashMemoizable;

    [Fact]
    public void FastWriterMatchesReferenceForDuplicateNamesEscapesUnicodeAndEveryAsciiCharacter()
    {
        var cases = new[]
        {
            "{\"z\":1,\"a\":2,\"z\":3,\"a\":4}",
            "{\"z\":[-0,1e+03,1.000,1E-5,123456789012345678901234567890],\"a\":0.0}",
            "{\"å\":\"ação 😀\",\"a\":\"\\u0061\",\"<tag>\":\"\\/\"}",
            JsonSerializer.Serialize(Enumerable.Range(0, 128).Select(value => ((char)value).ToString()).ToArray()),
            JsonSerializer.Serialize(new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()))
        };
        foreach (var input in cases)
        {
            using var json = JsonDocument.Parse(input);
            var reference = CanonicalJson.Serialize(json.RootElement);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(reference))), CanonicalJson.ComputeHash(json.RootElement));
        }
        for (var character = 32; character <= 126; character++)
        {
            if (character is 34 or 92) continue;
            using var literal = JsonDocument.Parse("\"" + (char)character + "\"");
            var reference = CanonicalJson.Serialize(literal.RootElement);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(reference))), CanonicalJson.ComputeHash(literal.RootElement));
        }
    }

    [Fact]
    public void StructurallySharedAggregatesMatchTheUnoptimizedSerializerForEveryRevision()
    {
        using var diagnostics = Core.Diagnostics.OperationalTiming.Begin();
        var manifest = new Core.Content.ContentManifest { Revision = "same-content", ConfigName = "test" };
        var combat = new Core.Combat.Models.CombatState();
        var run = new Core.Run.RunState
        {
            ContentManifest = manifest,
            Encounters = [new() { NodeId = "past", Combat = combat, Resolved = true }],
            Metadata = System.Collections.Immutable.ImmutableDictionary<string, JsonElement>.Empty
                .Add("<escaped>", JsonSerializer.SerializeToElement(new { Value = "ação", Amount = -0.0 }))
        };
        using var scope = CanonicalJson.BeginHashScope();
        for (var revision = 0; revision < 600; revision++)
        {
            var next = run with { Sequence = revision, ModeId = revision % 2 == 0 ? null : "mode" };
            Assert.Equal(CanonicalJson.Serialize(next), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("run", next).Bytes.Span));
            using var nested = CanonicalJson.BeginHashScope();
            var actorRevision = combat with { CurrentTurn = revision };
            Assert.Equal(CanonicalJson.Serialize(actorRevision), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("combat", actorRevision).Bytes.Span));
        }
        Assert.Contains("canonical_aggregate_encode", diagnostics.Snapshot().Keys);
        Assert.Contains("canonical_encounters_encode", diagnostics.Snapshot().Keys);
    }

    [Fact]
    public void StructurallySharedEncodingRetainsDepthAndDeclaredTypeSemantics()
    {
        using var scope = CanonicalJson.BeginHashScope();
        using var document = JsonDocument.Parse(new string('[', 63) + "0" + new string(']', 63));
        var invalid = new Core.Run.RunState { Metadata =
            System.Collections.Immutable.ImmutableDictionary<string, JsonElement>.Empty.Add("deep", document.RootElement.Clone()) };
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.Serialize(invalid));
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.Property("run", invalid));
        var run = new Core.Run.RunState();
        Assert.Equal(CanonicalJson.Serialize<object>(run), System.Text.Encoding.UTF8.GetString(
            CanonicalJson.Property<object>("run", run).Bytes.Span));
    }

    [Fact]
    public void ActorRosterAndPolymorphicComponentsRetainExactBytesWhenOneActorChanges()
    {
        var actor = new Core.Combat.Models.CombatActorState
        {
            InstanceId = "hero", Name = "Ação <hero>",
            Components = System.Collections.Immutable.ImmutableDictionary<string, Core.Combat.Models.EntityComponentState>.Empty
                .Add("stats", new Core.Combat.Models.StatEntityComponentState { ComponentId = "stats", Values =
                    new Dictionary<string, float> { ["armor"] = 100, ["critical"] = 150 } })
        };
        var other = actor with { InstanceId = "enemy" };
        using var scope = CanonicalJson.BeginHashScope();
        for (var revision = 0; revision < 100; revision++)
        {
            var changed = actor with { Name = $"hero-{revision}" };
            var combat = new Core.Combat.Models.CombatState { Actors =
                new Dictionary<string, Core.Combat.Models.CombatActorState> { ["z"] = other, ["<hero>"] = changed } };
            Assert.Equal(CanonicalJson.Serialize(combat), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("combat", combat).Bytes.Span));
            var run = new Core.Run.RunState { PlayerEntity = changed };
            Assert.Equal(CanonicalJson.Serialize(run), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("run", run).Bytes.Span));
        }
    }

    [Fact]
    public void AppendedHistoryRetainsEveryRecordAndMatchesTheOriginalSerializer()
    {
        using var scope = CanonicalJson.BeginHashScope();
        using var diagnostics = Core.Diagnostics.OperationalTiming.Begin();
        var history = System.Collections.Immutable.ImmutableList<Core.Combat.Models.CombatAction>.Empty;
        for (var revision = 0; revision < 64; revision++)
        {
            history = history.Add(new() { ActorId = "hero", PowerId = $"action-{revision}", Turn = revision,
                Timestamp = DateTime.UnixEpoch.AddSeconds(revision), TargetIds = ["a", "b"] });
            var combat = new Core.Combat.Models.CombatState { ActionHistory = history };
            Assert.Equal(CanonicalJson.Serialize(combat), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("combat", combat).Bytes.Span));
        }
        Assert.Contains("canonical_aggregate_encode", diagnostics.Snapshot().Keys);
    }

    [Fact]
    public void ActivationRevisionsRetainExactIntentBytes()
    {
        using var scope = CanonicalJson.BeginHashScope();
        var activation = new Core.Combat.Activation.ActivationState
        {
            ActiveActorId = "hero", ActivationOrder = ["hero", "enemy"],
            CompletedActorIds = ["<old>"], StartedAtUtc = DateTime.UnixEpoch,
            Intents = [new() { ActorId = "enemy", DisplayName = "Ação <enemy>",
                TargetIds = ["hero"], Tags = ["fire"], PreviewUncertain = true }]
        };
        for (var revision = 0; revision < 100; revision++)
        {
            var changed = activation with { ActionsTaken = revision, RulesId = revision % 2 == 0 ? null : "rules" };
            var combat = new Core.Combat.Models.CombatState { ActivationState = changed };
            Assert.Equal(CanonicalJson.Serialize(combat), System.Text.Encoding.UTF8.GetString(
                CanonicalJson.Property("combat", combat).Bytes.Span));
        }
        var replaced = activation with { Intents = [activation.Intents[0] with { PreviewUncertain = false }] };
        Assert.NotEqual(CanonicalJson.Serialize(activation), CanonicalJson.Serialize(replaced));
        var final = new Core.Combat.Models.CombatState { ActivationState = replaced };
        Assert.Equal(CanonicalJson.Serialize(final), System.Text.Encoding.UTF8.GetString(
            CanonicalJson.Property("combat", final).Bytes.Span));
    }

    [Fact]
    public void CanonicalFragmentsOwnTheirBytesAfterTemporaryDocumentsAreDisposedAndBuffersReused()
    {
        using var scope = CanonicalJson.BeginHashScope();
        var run = new Core.Run.RunState { ModeId = "ação <fire>", Sequence = 3 };
        var fragment = CanonicalJson.Property("run", run);
        var reference = CanonicalJson.Serialize(run);
        for (var index = 0; index < 300; index++)
        {
            _ = CanonicalJson.ComputeHash(new { text = new string('x', 4096), index });
            _ = CanonicalJson.Property("run", run with { Sequence = index + 4 });
        }
        GC.Collect();
        Assert.Equal(reference, System.Text.Encoding.UTF8.GetString(fragment.Bytes.Span));
        Assert.Equal(CanonicalJson.ComputeHash(new { run }), CanonicalJson.ComputeObjectHash(fragment));
    }

    [Fact]
    public void ComposedFingerprintMatchesEmbeddedCanonicalJsonWithColdWarmAndEvictedBytes()
    {
        var run = new Core.Run.RunState();
        var combat = new Core.Combat.Models.CombatState();
        var steps = new[] { "<fire>", "ação", "\"quoted\"" };
        var expected = CanonicalJson.ComputeHash(new { state = combat, steps, run });
        string Composed() => CanonicalJson.ComputeObjectHash(CanonicalJson.Property("steps", steps),
            CanonicalJson.Property("run", run), CanonicalJson.Property("state", combat));
        Assert.Equal(expected, Composed());
        using (CanonicalJson.BeginHashScope())
        {
            Assert.Equal(expected, Composed());
            Assert.Equal(expected, Composed());
            for (var index = 0; index < 80; index++)
                _ = CanonicalJson.Property("other", combat with { CurrentTurn = index });
            Assert.Equal(expected, Composed());
        }
        Assert.Equal(expected, Composed());
        Assert.Equal(CanonicalJson.ComputeHash(new { run = (Core.Run.RunState?)null }),
            CanonicalJson.ComputeObjectHash(CanonicalJson.Property("run", (Core.Run.RunState?)null)));
    }

    [Fact]
    public void CanonicalNumbersPreserveOriginalLexemesAndMatchLegacyWriter()
    {
        using var document = JsonDocument.Parse("{\"z\":[-0,1e+03,1.000,1E-5,123456789012345678901234567890],\"a\":0.0}");
        const string expected = "{\"a\":0.0,\"z\":[-0,1e+03,1.000,1E-5,123456789012345678901234567890]}";
        Assert.Equal(expected, CanonicalJson.Serialize(document.RootElement));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expected))), CanonicalJson.ComputeHash(document.RootElement));
    }

    [Fact]
    public void ComposedFingerprintPreservesTheDefaultSerializationDepthLimit()
    {
        using var document = JsonDocument.Parse(new string('[', 64) + "0" + new string(']', 64));
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.ComputeHash(new { value = document.RootElement }));
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.ComputeObjectHash(
            CanonicalJson.Property("value", document.RootElement)));
        using var allowed = JsonDocument.Parse(new string('[', 63) + "0" + new string(']', 63));
        Assert.Equal(CanonicalJson.ComputeHash(new { value = allowed.RootElement }),
            CanonicalJson.ComputeObjectHash(CanonicalJson.Property("value", allowed.RootElement)));
    }

    [Fact]
    public void WeakDefaultMemoMatchesCanonicalBytesAndNeverMemoizesMutableObjects()
    {
        var value = new MemoizableValue(42);
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(CanonicalJson.Serialize(value))));
        Parallel.For(0, 32, _ => Assert.Equal(expected, CanonicalJson.ComputeHash(value)));
        var mutable = new Dictionary<string, int> { ["x"] = 1 };
        var before = CanonicalJson.ComputeHash(mutable);
        mutable["x"] = 2;
        Assert.NotEqual(before, CanonicalJson.ComputeHash(mutable));
        Assert.NotEqual(expected, CanonicalJson.ComputeHash(value with { Value = 43 }));
    }

    private sealed class CountingConverter : JsonConverter<MemoizableValue>
    {
        public int Writes { get; private set; }

        public override MemoizableValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            new(reader.GetInt32());

        public override void Write(
            Utf8JsonWriter writer,
            MemoizableValue value,
            JsonSerializerOptions options)
        {
            Writes++;
            writer.WriteNumberValue(value.Value);
        }
    }
}
