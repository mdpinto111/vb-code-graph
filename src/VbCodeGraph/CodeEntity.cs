using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace VbCodeGraph;

public static class EntityKind
{
    public const string Class = "Class";
    public const string Module = "Module";
    public const string Function = "Function";
    public const string Sub = "Sub";
    public const string Constant = "Constant";

    public static bool IsMethod(string kind) => kind is Function or Sub;
}

/// <summary>A named piece of code (class, module, method or constant) and its links to other entities.</summary>
public sealed class CodeEntity
{
    public string Kind { get; set; } = "";

    /// <summary>Qualified name: Namespace.Class.Member, Module.Member, or JS.name for JavaScript.</summary>
    public string Name { get; set; } = "";

    public string File { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Code { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Parameters { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReturnsValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    /// <summary>Methods this entity calls.</summary>
    public List<string> Calls { get; set; } = [];

    /// <summary>Entities that call this one.</summary>
    public List<string> CalledBy { get; set; } = [];

    /// <summary>Classes this entity instantiates with New.</summary>
    public List<string> Instantiates { get; set; } = [];

    /// <summary>Constants read by this method, with their values.</summary>
    public SortedDictionary<string, string> ConstantsUsed { get; set; } = [];

    [JsonIgnore]
    public SyntaxNode? Node { get; set; }

    /// <summary>The enclosing class or module, e.g. "Shop.Billing" for "Shop.Billing.Total".</summary>
    [JsonIgnore]
    public string Scope => Name.Contains('.') ? Name[..Name.LastIndexOf('.')] : "";

    [JsonIgnore]
    public string SimpleName => Name.Contains('.') ? Name[(Name.LastIndexOf('.') + 1)..] : Name;
}
