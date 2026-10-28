using Core.GameplayEvents;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class HierarchySelectorTests
{
    [Theory]
    [InlineData("resource.change.resolved", "resource.change.resolved", true)]
    [InlineData("resource.*", "resource.change.resolved", false)]
    [InlineData("resource.*", "resource.change", true)]
    [InlineData("resource.*.resolved", "resource.change.resolved", true)]
    [InlineData("resource.*.resolved", "resource.change.current.resolved", false)]
    [InlineData("resource.**", "resource", false)]
    [InlineData("resource.**", "resource.change", true)]
    [InlineData("resource.**", "resource.change.current.resolved", true)]
    [InlineData("**", "resource", true)]
    [InlineData("*", "resource", true)]
    [InlineData("*", "resource.change", false)]
    public void HierarchyHasExplicitSegmentAndDescendantSemantics(string pattern, string topic, bool expected)
        => Assert.Equal(expected, HierarchyPattern.Compile(pattern).Value.Matches(HierarchicalName.Parse(topic).Value));

    [Theory]
    [InlineData("")]
    [InlineData(".resource")]
    [InlineData("resource.")]
    [InlineData("resource..change")]
    [InlineData("Resource.change")]
    [InlineData("résource.change")]
    [InlineData("resource.**.resolved")]
    [InlineData("resource.dam*")]
    [InlineData("resource/changed")]
    [InlineData("resource.!")]
    public void InvalidPatternsAreRejectedBeforeIndexing(string pattern) => Assert.True(HierarchyPattern.Compile(pattern).IsFailure);

    [Fact]
    public void IndexDeduplicatesOverlapsAndExclusionWinsWithoutChangingIdentityOrder()
    {
        var all = HierarchySelector.Compile(["**", "resource.**", "resource.*.resolved"], ["resource.limit.**"]).Value;
        var precise = HierarchySelector.Compile(["resource.change.resolved"]).Value;
        var registrations = new[] { new HierarchyIndexRegistration("z", all), new HierarchyIndexRegistration("a", precise) };
        foreach (var input in new[] { registrations, registrations.Reverse().ToArray() })
        {
            var index = HierarchySelectorIndex.Build(input).Value;
            Assert.Equal(new[] { "a", "z" }, index.Match("resource.change.resolved").Value);
            Assert.Empty(index.Match("resource.limit.applied").Value);
            Assert.Equal(new[] { "z" }, index.Match("status.applied").Value);
            Assert.True(index.Match("Resource.change").IsFailure);
        }
    }

    [Fact]
    public void AuthoredListsAreCopiedAndOrderDoesNotAffectMatching()
    {
        var includes = new[] { "status.*", "resource.**" };
        var excludes = new[] { "status.removed" };
        var original = HierarchySelector.Compile(includes, excludes).Value;
        var reordered = HierarchySelector.Compile(includes.Reverse(), excludes.Reverse()).Value;
        includes[0] = "**"; excludes[0] = "**";
        foreach (var name in new[] { "status.applied", "status.removed", "resource.change.resolved", "actor.defeated" })
            Assert.Equal(original.Matches(HierarchicalName.Parse(name).Value), reordered.Matches(HierarchicalName.Parse(name).Value));
        Assert.False(original.Matches(HierarchicalName.Parse("actor.defeated").Value));
    }

    [Fact]
    public void BudgetsAndDuplicatesAreValidated()
    {
        Assert.True(HierarchicalName.Parse(new string('a', 257)).IsFailure);
        Assert.True(HierarchyPattern.Compile(string.Join('.', Enumerable.Repeat("a", 17))).IsFailure);
        Assert.True(HierarchySelector.Compile(Enumerable.Repeat("**", 65)).IsFailure);
        Assert.True(HierarchySelector.Compile([]).IsFailure);
        Assert.True(HierarchicalName.Parse("resource.*").IsFailure);
        var registration = new HierarchyIndexRegistration("same", HierarchySelector.Compile(["**"]).Value);
        Assert.True(HierarchySelectorIndex.Build([registration, registration]).IsFailure);
        Assert.True(HierarchySelectorIndex.Build(Enumerable.Repeat(registration, 4097)).IsFailure);
    }
}
