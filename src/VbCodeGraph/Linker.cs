using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VbCodeGraph;

/// <summary>
/// Links entities together: which methods each entity calls, which classes it creates
/// and which constants it reads.
/// </summary>
/// <remarks>
/// Resolution is syntactic, without a semantic model: legacy projects rarely compile
/// outside their original environment. A name is resolved in this order: an entity of
/// the same class or module, then an exact qualified name, then the only entity with
/// that simple name. When several unrelated entities share a simple name, all of them
/// are linked, so the graph errs on the side of showing too much rather than too little.
/// </remarks>
public static partial class Linker
{
    [GeneratedRegex(@"\b([A-Za-z_$][\w$]*)\s*\(")]
    private static partial Regex JsCall();

    [GeneratedRegex(@"\b([A-Z_][A-Z0-9_]*)\b")]
    private static partial Regex JsConstantReference();

    public static void Link(IReadOnlyList<CodeEntity> entities)
    {
        var methods = Index(entities.Where(e => EntityKind.IsMethod(e.Kind)));
        var classes = Index(entities.Where(e => e.Kind == EntityKind.Class));
        var constants = Index(entities.Where(e => e.Kind == EntityKind.Constant));
        var byName = entities.Where(e => e.Kind != EntityKind.Constant)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities.Where(e => e.Kind != EntityKind.Constant))
        {
            if (entity.Language == "JavaScript")
            {
                LinkJavaScript(entity, methods, constants);
            }
            else if (entity.Node is not null)
            {
                LinkVisualBasic(entity, methods, classes, constants);
            }
        }

        foreach (var caller in entities)
        {
            foreach (var callee in caller.Calls)
            {
                if (byName.TryGetValue(callee, out var target) && !target.CalledBy.Contains(caller.Name))
                {
                    target.CalledBy.Add(caller.Name);
                }
            }
        }
    }

    static void LinkVisualBasic(CodeEntity entity, Lookup methods, Lookup classes, Lookup constants)
    {
        var node = entity.Node!;
        // For a class or module, only constructors and field initializers belong to it:
        // its methods are separate entities. For a method, its whole body counts.
        var isContainer = entity.Kind is EntityKind.Class or EntityKind.Module;
        var scope = isContainer ? entity.Name : entity.Scope;
        var bodies = isContainer
            ? node.ChildNodes().Where(n => n is ConstructorBlockSyntax or FieldDeclarationSyntax).ToList()
            : [node];

        // Parameters and local variables shadow members with the same name, as in VB.
        var locals = bodies.SelectMany(b => b.DescendantNodes())
            .Where(n => n is ParameterSyntax || n.Parent is VariableDeclaratorSyntax { Parent: LocalDeclarationStatementSyntax })
            .Select(n => n switch
            {
                ParameterSyntax p => p.Identifier.Identifier.Text,
                ModifiedIdentifierSyntax m => m.Identifier.Text,
                _ => "",
            })
            .Concat(bodies.SelectMany(b => b.DescendantNodes()).OfType<ForEachStatementSyntax>()
                .Select(f => f.ControlVariable is VariableDeclaratorSyntax d ? d.Names[0].Identifier.Text : f.ControlVariable.ToString()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var names = bodies.SelectMany(b => b.DescendantNodes()).OfType<IdentifierNameSyntax>()
            .Select(i => i.Identifier.Text)
            .Where(n => !locals.Contains(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            foreach (var target in methods.Resolve(name, scope))
            {
                // A VB function assigns its result to its own name: that is not a call.
                if (!string.Equals(target.Name, entity.Name, StringComparison.OrdinalIgnoreCase))
                {
                    AddOnce(entity.Calls, target.Name);
                }
            }
            if (EntityKind.IsMethod(entity.Kind))
            {
                foreach (var constant in constants.Resolve(name, scope))
                {
                    entity.ConstantsUsed.TryAdd(constant.Name, constant.Value ?? "");
                }
            }
        }

        // Qualified access such as Settings.MaxItems resolves exactly.
        foreach (var access in bodies.SelectMany(b => b.DescendantNodes()).OfType<MemberAccessExpressionSyntax>())
        {
            if (EntityKind.IsMethod(entity.Kind) && constants.TryExact(access.ToString(), out var constant))
            {
                entity.ConstantsUsed.TryAdd(constant.Name, constant.Value ?? "");
            }
        }

        foreach (var creation in bodies.SelectMany(b => b.DescendantNodesAndSelf()).OfType<ObjectCreationExpressionSyntax>())
        {
            var typeName = creation.Type is GenericNameSyntax g ? g.Identifier.Text : creation.Type.ToString();
            foreach (var cls in classes.Resolve(typeName, scope))
            {
                AddOnce(entity.Instantiates, cls.Name);
            }
        }
    }

    static void LinkJavaScript(CodeEntity entity, Lookup methods, Lookup constants)
    {
        // Skip the declaration line so a function is not linked to itself.
        var body = entity.Code.Contains('\n') ? entity.Code[entity.Code.IndexOf('\n')..] : "";
        foreach (Match m in JsCall().Matches(body))
        {
            if (methods.TryExact($"JS.{m.Groups[1].Value}", out var target) && target.Name != entity.Name)
            {
                AddOnce(entity.Calls, target.Name);
            }
        }
        foreach (Match m in JsConstantReference().Matches(entity.Code))
        {
            if (constants.TryExact($"JS.{m.Groups[1].Value}", out var constant))
            {
                entity.ConstantsUsed.TryAdd(constant.Name, constant.Value ?? "");
            }
        }
    }

    static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    static Lookup Index(IEnumerable<CodeEntity> entities) => new(entities.ToList());

    sealed class Lookup(List<CodeEntity> entities)
    {
        // VB.NET identifiers are case-insensitive.
        readonly Dictionary<string, CodeEntity> exact = entities
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        readonly ILookup<string, CodeEntity> bySimpleName = entities
            .Where(e => e.Language is null)
            .ToLookup(e => e.SimpleName, StringComparer.OrdinalIgnoreCase);

        public bool TryExact(string name, out CodeEntity entity) => exact.TryGetValue(name, out entity!);

        public IEnumerable<CodeEntity> Resolve(string name, string scope)
        {
            if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                return [];
            }
            var candidates = bySimpleName[name].ToList();
            var sameScope = candidates.FirstOrDefault(c => string.Equals(c.Scope, scope, StringComparison.OrdinalIgnoreCase));
            if (sameScope is not null)
            {
                return [sameScope];
            }
            if (exact.TryGetValue(name, out var qualified))
            {
                return [qualified];
            }
            return candidates;
        }
    }
}
