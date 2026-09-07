namespace Mods;

public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Trim().Split('-', 2, StringSplitOptions.None);
        var numbers = parts[0].Split('.');
        if (numbers.Length is < 1 or > 3 ||
            !int.TryParse(numbers[0], out var major) || major < 0 ||
            (numbers.Length > 1 && (!int.TryParse(numbers[1], out var minor) || minor < 0)) ||
            (numbers.Length > 2 && (!int.TryParse(numbers[2], out var patch) || patch < 0)))
        {
            return false;
        }
        var resolvedMinor = numbers.Length > 1 ? int.Parse(numbers[1]) : 0;
        var resolvedPatch = numbers.Length > 2 ? int.Parse(numbers[2]) : 0;
        var preRelease = parts.Length == 2 ? parts[1] : null;
        if (preRelease != null && (preRelease.Length == 0 ||
            preRelease.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))))
        {
            return false;
        }
        version = new SemanticVersion(major, resolvedMinor, resolvedPatch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (PreRelease == null && other.PreRelease != null) return 1;
        if (PreRelease != null && other.PreRelease == null) return -1;
        return string.Compare(PreRelease, other.PreRelease, StringComparison.Ordinal);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}{(PreRelease == null ? string.Empty : $"-{PreRelease}")}";
}

public sealed class SemanticVersionRange
{
    private readonly IReadOnlyList<Func<SemanticVersion, bool>> _constraints;

    private SemanticVersionRange(IReadOnlyList<Func<SemanticVersion, bool>> constraints)
    {
        _constraints = constraints;
    }

    public bool Contains(SemanticVersion version) => _constraints.All(constraint => constraint(version));

    public static bool TryParse(string? value, out SemanticVersionRange range)
    {
        range = new SemanticVersionRange([]);
        var text = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
        if (text is "*" or "x" or "X")
            return true;

        if (text.StartsWith('^') || text.StartsWith('~'))
        {
            var kind = text[0];
            if (!SemanticVersion.TryParse(text[1..], out var minimum))
                return false;
            var maximum = kind == '^'
                ? minimum.Major > 0
                    ? new SemanticVersion(minimum.Major + 1, 0, 0)
                    : minimum.Minor > 0
                        ? new SemanticVersion(0, minimum.Minor + 1, 0)
                        : new SemanticVersion(0, 0, minimum.Patch + 1)
                : new SemanticVersion(minimum.Major, minimum.Minor + 1, 0);
            range = new SemanticVersionRange([
                candidate => candidate.CompareTo(minimum) >= 0,
                candidate => candidate.CompareTo(maximum) < 0
            ]);
            return true;
        }

        if (text.Contains('x', StringComparison.OrdinalIgnoreCase) || text.Contains('*'))
        {
            var parts = text.Split('.');
            if (parts.Length > 3 || !int.TryParse(parts[0], out var major) || major < 0)
                return false;
            if (parts.Length == 1 || parts[1] is "x" or "X" or "*")
            {
                range = new SemanticVersionRange([candidate => candidate.Major == major]);
                return true;
            }
            if (!int.TryParse(parts[1], out var minor) || minor < 0)
                return false;
            range = new SemanticVersionRange([
                candidate => candidate.Major == major && candidate.Minor == minor
            ]);
            return true;
        }

        if (!text.Contains(' ') && !text.StartsWith('>') && !text.StartsWith('<') && !text.StartsWith('='))
        {
            if (!SemanticVersion.TryParse(text, out var exact))
                return false;
            range = new SemanticVersionRange([candidate => candidate.CompareTo(exact) == 0]);
            return true;
        }

        var constraints = new List<Func<SemanticVersion, bool>>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var operation = token.StartsWith(">=") || token.StartsWith("<=")
                ? token[..2]
                : token.StartsWith('>') || token.StartsWith('<') || token.StartsWith('=')
                    ? token[..1]
                    : string.Empty;
            if (operation.Length == 0 || !SemanticVersion.TryParse(token[operation.Length..], out var boundary))
                return false;
            constraints.Add(operation switch
            {
                ">=" => candidate => candidate.CompareTo(boundary) >= 0,
                "<=" => candidate => candidate.CompareTo(boundary) <= 0,
                ">" => candidate => candidate.CompareTo(boundary) > 0,
                "<" => candidate => candidate.CompareTo(boundary) < 0,
                "=" => candidate => candidate.CompareTo(boundary) == 0,
                _ => throw new InvalidOperationException()
            });
        }
        range = new SemanticVersionRange(constraints);
        return constraints.Count > 0;
    }
}
