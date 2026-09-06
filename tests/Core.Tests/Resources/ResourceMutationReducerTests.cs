using Core.Resources;
using Xunit;

namespace Core.Tests.Resources;

public sealed class ResourceMutationReducerTests
{
    private readonly ResourceMutationReducer _reducer = new();

    [Fact]
    public void Apply_IsAtomicWhenLaterMutationIsInvalid()
    {
        var resources = Resources(("mana", 10, 0, 10));

        var result = _reducer.Apply(resources,
        [
            Mutation("spend", "mana", ResourceValueField.Current, ResourceMutationOperation.Subtract, 3),
            Mutation("missing", "rage", ResourceValueField.Current, ResourceMutationOperation.Add, 2)
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal(10, resources["mana"].Current);
    }

    [Fact]
    public void Apply_CanChangeMaximumAndClampsCurrent()
    {
        var resources = Resources(("mana", 8, 0, 10));

        var result = _reducer.Apply(resources,
        [
            Mutation("lower-cap", "mana", ResourceValueField.Maximum, ResourceMutationOperation.Set, 5)
        ]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(5, result.Value.Resources["mana"].Maximum);
        Assert.Equal(5, result.Value.Resources["mana"].Current);
        Assert.Equal(ResourceValueField.Maximum, result.Value.Records[0].Field);
    }

    [Fact]
    public void Apply_RejectsCrossedBoundsWithoutChangingInput()
    {
        var resources = Resources(("mana", 8, 0, 10));

        var result = _reducer.Apply(resources,
        [
            Mutation("bad-min", "mana", ResourceValueField.Minimum, ResourceMutationOperation.Set, 11)
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal(0, resources["mana"].Minimum);
    }

    [Theory]
    [InlineData(ResourceMutationOperation.Add)]
    [InlineData(ResourceMutationOperation.Subtract)]
    public void Apply_RejectsNegativeMagnitudeInsteadOfSilentlyChangingItsSign(ResourceMutationOperation operation)
    {
        var resources = Resources(("mana", 5, 0, 10));
        Assert.True(_reducer.Apply(resources, [Mutation("negative", "mana", ResourceValueField.Current, operation, -2)]).IsFailure);
        Assert.Equal(5, resources["mana"].Current);
    }

    private static ResolvedResourceMutation Mutation(
        string id,
        string resourceId,
        ResourceValueField field,
        ResourceMutationOperation operation,
        float value) => new()
    {
        MutationId = id,
        ResourceId = resourceId,
        Field = field,
        Operation = operation,
        Value = value
    };

    private static IReadOnlyDictionary<string, ResourcePool> Resources(
        params (string Id, float Current, float Min, float Max)[] values) =>
        values.ToDictionary(value => value.Id, value =>
        {
            var definition = new ResourceDefinition
            {
                ResourceId = value.Id,
                DisplayName = value.Id,
                DefaultCurrent = value.Current,
                DefaultMin = value.Min,
                DefaultMax = value.Max
            };
            return new ResourcePool
            {
                ResourceId = value.Id,
                Current = value.Current,
                Minimum = value.Min,
                Maximum = value.Max,
                Definition = definition
            };
        }, StringComparer.Ordinal);
}
