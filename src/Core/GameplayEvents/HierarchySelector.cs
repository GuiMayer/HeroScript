using System.Collections.Immutable;
using Core.Common;

namespace Core.GameplayEvents;

/// <summary>Ordinal addresses for authored topics; never interprets game semantics.</summary>
public sealed record HierarchicalName
{
    public const int MaximumLength = 256;
    public const int MaximumDepth = 16;
    public string Value { get; }
    public ImmutableArray<string> Segments { get; }
    private HierarchicalName(string value, ImmutableArray<string> segments) => (Value, Segments) = (value, segments);

    public static Result<HierarchicalName> Parse(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
            return Result<HierarchicalName>.Failure("Hierarchy name must contain 1..256 characters");
        var segments = value.Split('.');
        if (segments.Length > MaximumDepth || segments.Any(segment => !ValidSegment(segment)))
            return Result<HierarchicalName>.Failure("Hierarchy segments require lowercase ASCII identifiers, depth <= 16");
        return Result<HierarchicalName>.Success(new(value, segments.ToImmutableArray()));
    }

    internal static bool ValidSegment(string segment) => segment.Length > 0 && segment[0] is >= 'a' and <= 'z' &&
        segment.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    public override string ToString() => Value;
}

public sealed class HierarchyPattern
{
    public string Value { get; }
    public ImmutableArray<string> Segments { get; }
    private HierarchyPattern(string value, ImmutableArray<string> segments) => (Value, Segments) = (value, segments);

    public static Result<HierarchyPattern> Compile(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > HierarchicalName.MaximumLength)
            return Result<HierarchyPattern>.Failure("Hierarchy pattern must contain 1..256 characters");
        var segments = value.Split('.');
        if (segments.Length > HierarchicalName.MaximumDepth)
            return Result<HierarchyPattern>.Failure("Hierarchy pattern exceeds depth limit");
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment == "**" && index != segments.Length - 1)
                return Result<HierarchyPattern>.Failure("Recursive wildcard ** is only valid at the end");
            if (segment is not ("*" or "**") && !HierarchicalName.ValidSegment(segment))
                return Result<HierarchyPattern>.Failure("Invalid hierarchy pattern segment");
        }
        return Result<HierarchyPattern>.Success(new(value, segments.ToImmutableArray()));
    }

    public bool Matches(HierarchicalName name)
    {
        for (var index = 0; index < Segments.Length; index++)
        {
            // ** means one or more descendants: resource.** never matches resource itself.
            if (index >= name.Segments.Length) return false;
            if (Segments[index] == "**") return true;
            if (Segments[index] != "*" && !StringComparer.Ordinal.Equals(Segments[index], name.Segments[index])) return false;
        }
        return Segments.Length == name.Segments.Length;
    }
}

/// <summary>Include is OR, exclude wins. A matching selector represents one subscription, not N patterns.</summary>
public sealed class HierarchySelector
{
    public const int MaximumPatterns = 64;
    public ImmutableArray<HierarchyPattern> Include { get; }
    public ImmutableArray<HierarchyPattern> Exclude { get; }
    private HierarchySelector(ImmutableArray<HierarchyPattern> include, ImmutableArray<HierarchyPattern> exclude)
        => (Include, Exclude) = (include, exclude);

    public static Result<HierarchySelector> Compile(IEnumerable<string>? include, IEnumerable<string>? exclude = null)
    {
        var included = CompileList(include);
        if (included.IsFailure) return Result<HierarchySelector>.Failure(included.Error);
        if (included.Value.IsEmpty) return Result<HierarchySelector>.Failure("At least one include pattern is required");
        var excluded = CompileList(exclude ?? []);
        return excluded.IsFailure ? Result<HierarchySelector>.Failure(excluded.Error)
            : Result<HierarchySelector>.Success(new(included.Value, excluded.Value));
    }

    private static Result<ImmutableArray<HierarchyPattern>> CompileList(IEnumerable<string>? values)
    {
        if (values == null) return Result<ImmutableArray<HierarchyPattern>>.Failure("Pattern list is required");
        var snapshot = values.Take(MaximumPatterns + 1).ToArray();
        if (snapshot.Length > MaximumPatterns) return Result<ImmutableArray<HierarchyPattern>>.Failure("Pattern count exceeds limit");
        var result = ImmutableArray.CreateBuilder<HierarchyPattern>();
        foreach (var value in snapshot.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var pattern = HierarchyPattern.Compile(value);
            if (pattern.IsFailure) return Result<ImmutableArray<HierarchyPattern>>.Failure(pattern.Error);
            result.Add(pattern.Value);
        }
        return Result<ImmutableArray<HierarchyPattern>>.Success(result.ToImmutable());
    }

    public bool Matches(HierarchicalName name) => Include.Any(pattern => pattern.Matches(name)) &&
        !Exclude.Any(pattern => pattern.Matches(name));
}
