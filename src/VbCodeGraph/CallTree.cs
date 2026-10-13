using System.Text;
using System.Text.Json.Serialization;

namespace VbCodeGraph;

public sealed class CallTreeNode
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    public List<CallTreeNode> Calls { get; init; } = [];
}

/// <summary>Builds the tree of everything an entry point calls, directly or indirectly.</summary>
public static class CallTree
{
    public const string Recursive = "Recursive";
    public const string Unknown = "Unknown";

    public static CallTreeNode Build(AnalysisResult analysis, string entryPoint, int maxDepth = 50)
    {
        var lookup = analysis.All
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var name = Find(lookup, entryPoint);
        return Visit(name, lookup, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, maxDepth);
    }

    /// <summary>Accepts a qualified name, or a simple name when it is unambiguous.</summary>
    static string Find(Dictionary<string, CodeEntity> lookup, string entryPoint)
    {
        if (lookup.ContainsKey(entryPoint))
        {
            return entryPoint;
        }
        var matches = lookup.Values
            .Where(e => string.Equals(e.SimpleName, entryPoint, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException($"No entity named '{entryPoint}'."),
            _ => throw new ArgumentException($"'{entryPoint}' is ambiguous: {string.Join(", ", matches)}"),
        };
    }

    static CallTreeNode Visit(string name, Dictionary<string, CodeEntity> lookup, HashSet<string> path, int depth, int maxDepth)
    {
        if (!lookup.TryGetValue(name, out var entity))
        {
            return new CallTreeNode { Name = name, Kind = Unknown, Note = "not found in the analysis" };
        }
        if (path.Contains(entity.Name))
        {
            return new CallTreeNode { Name = entity.Name, Kind = Recursive, Note = "already on the call path" };
        }
        if (depth >= maxDepth)
        {
            return new CallTreeNode { Name = entity.Name, Kind = entity.Kind, File = entity.File, Note = "max depth reached" };
        }

        path.Add(entity.Name);
        var node = new CallTreeNode
        {
            Name = entity.Name,
            Kind = entity.Kind,
            File = entity.File,
            Calls = entity.Calls.Select(c => Visit(c, lookup, path, depth + 1, maxDepth)).ToList(),
        };
        path.Remove(entity.Name);
        return node;
    }

    public static string Render(CallTreeNode root)
    {
        var sb = new StringBuilder();
        Render(root, sb, "", "", "");
        return sb.ToString();
    }

    static void Render(CallTreeNode node, StringBuilder sb, string indent, string branch, string childIndent)
    {
        sb.Append(indent).Append(branch).Append(node.Name);
        if (node.Note is not null)
        {
            sb.Append("  (").Append(node.Note).Append(')');
        }
        sb.AppendLine();
        for (var i = 0; i < node.Calls.Count; i++)
        {
            var last = i == node.Calls.Count - 1;
            Render(node.Calls[i], sb, indent + childIndent, last ? "└── " : "├── ", last ? "    " : "│   ");
        }
    }
}
