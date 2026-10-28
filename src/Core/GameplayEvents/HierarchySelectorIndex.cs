using System.Collections.Immutable;
using Core.Common;

namespace Core.GameplayEvents;

public sealed record HierarchyIndexRegistration(string RegistrationId, HierarchySelector Selector);

/// <summary>Immutable candidate trie. Returned IDs are canonical, not an execution priority.</summary>
public sealed class HierarchySelectorIndex
{
    public const int MaximumRegistrations = 4096;
    public const int MaximumNodes = 65536;
    private sealed record Node(ImmutableDictionary<string, Node> Children, ImmutableHashSet<string> Exact,
        ImmutableHashSet<string> Descendants);
    private sealed class Builder
    {
        internal readonly Dictionary<string, Builder> Children = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Exact = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Descendants = new(StringComparer.Ordinal);
        internal Node Freeze() => new(Children.ToImmutableDictionary(item => item.Key, item => item.Value.Freeze(), StringComparer.Ordinal),
            Exact.ToImmutableHashSet(StringComparer.Ordinal), Descendants.ToImmutableHashSet(StringComparer.Ordinal));
    }
    private readonly Node _root;
    private readonly ImmutableDictionary<string, HierarchySelector> _selectors;
    private HierarchySelectorIndex(Node root, ImmutableDictionary<string, HierarchySelector> selectors) => (_root, _selectors) = (root, selectors);

    public static Result<HierarchySelectorIndex> Build(IEnumerable<HierarchyIndexRegistration>? registrations)
    {
        if (registrations == null) return Result<HierarchySelectorIndex>.Failure("Registrations are required");
        var snapshot = registrations.Take(MaximumRegistrations + 1).ToArray();
        if (snapshot.Length > MaximumRegistrations || snapshot.Any(item => item == null || item.Selector == null ||
                string.IsNullOrWhiteSpace(item.RegistrationId) || item.RegistrationId.Length > 512) ||
            snapshot.Select(item => item.RegistrationId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            return Result<HierarchySelectorIndex>.Failure("Registration identities must be valid, unique and bounded");
        var root = new Builder();
        var nodes = 1;
        foreach (var registration in snapshot.OrderBy(item => item.RegistrationId, StringComparer.Ordinal))
        foreach (var pattern in registration.Selector.Include)
        {
            var node = root;
            foreach (var segment in pattern.Segments)
            {
                if (segment == "**") { node.Descendants.Add(registration.RegistrationId); break; }
                if (!node.Children.TryGetValue(segment, out var next))
                {
                    if (++nodes > MaximumNodes) return Result<HierarchySelectorIndex>.Failure("Hierarchy index exceeds node budget");
                    node.Children.Add(segment, next = new());
                }
                node = next;
            }
            if (pattern.Segments[^1] != "**") node.Exact.Add(registration.RegistrationId);
        }
        return Result<HierarchySelectorIndex>.Success(new(root.Freeze(), snapshot.ToImmutableDictionary(
            item => item.RegistrationId, item => item.Selector, StringComparer.Ordinal)));
    }

    public Result<ImmutableArray<string>> Match(string identifier)
    {
        var parsed = HierarchicalName.Parse(identifier);
        if (parsed.IsFailure) return Result<ImmutableArray<string>>.Failure(parsed.Error);
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        Visit(_root, 0);
        return Result<ImmutableArray<string>>.Success(candidates.Where(id => _selectors[id].Matches(parsed.Value))
            .Order(StringComparer.Ordinal).ToImmutableArray());

        void Visit(Node node, int depth)
        {
            if (depth == parsed.Value.Segments.Length) { candidates.UnionWith(node.Exact); return; }
            candidates.UnionWith(node.Descendants);
            if (node.Children.TryGetValue(parsed.Value.Segments[depth], out var exact)) Visit(exact, depth + 1);
            if (node.Children.TryGetValue("*", out var wildcard)) Visit(wildcard, depth + 1);
        }
    }
}
