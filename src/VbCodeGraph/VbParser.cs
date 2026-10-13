using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VbCodeGraph;

/// <summary>Extracts classes, modules, methods and constants from VB.NET source with Roslyn.</summary>
public static class VbParser
{
    public static List<CodeEntity> Parse(string code, string file)
    {
        var root = VisualBasicSyntaxTree.ParseText(code, path: file).GetRoot();
        var entities = new List<CodeEntity>();

        foreach (var cls in root.DescendantNodes().OfType<ClassBlockSyntax>())
        {
            // Keep the class code short: its declaration, fields and constructors.
            // Methods are separate entities with their own code.
            var summary = new StringBuilder().AppendLine(cls.ClassStatement.ToString().Trim());
            foreach (var member in cls.Members.Where(m => m is FieldDeclarationSyntax or ConstructorBlockSyntax))
            {
                summary.AppendLine(member.ToString().Trim());
            }
            entities.Add(Create(EntityKind.Class, QualifiedName(cls), file, cls, summary.ToString().Trim()));
        }

        foreach (var module in root.DescendantNodes().OfType<ModuleBlockSyntax>())
        {
            entities.Add(Create(EntityKind.Module, QualifiedName(module), file, module, module.ToString().Trim()));
        }

        foreach (var method in root.DescendantNodes().OfType<MethodBlockSyntax>())
        {
            var statement = method.SubOrFunctionStatement;
            var isFunction = statement.IsKind(SyntaxKind.FunctionStatement);
            var entity = Create(
                isFunction ? EntityKind.Function : EntityKind.Sub,
                $"{ContainerName(method)}.{statement.Identifier.Text}",
                file,
                method,
                method.ToString().Trim());
            entity.Parameters = statement.ParameterList?.Parameters.Select(p => p.Identifier.Identifier.Text).ToList() ?? [];
            entity.ReturnsValue = isFunction;
            entities.Add(entity);
        }

        // Constants, plus shared fields with an initializer (module-level or Shared), which
        // legacy code often uses as constants. Instance fields are state, not constants.
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);
            var isShared = field.Parent is ModuleBlockSyntax || field.Modifiers.Any(SyntaxKind.SharedKeyword);
            if (!isConst && !(isShared && field.Declarators.Any(d => d.Initializer is not null)))
            {
                continue;
            }
            foreach (var declarator in field.Declarators)
            {
                foreach (var name in declarator.Names)
                {
                    var entity = Create(
                        EntityKind.Constant,
                        $"{ContainerName(field)}.{name.Identifier.Text}",
                        file,
                        field,
                        field.ToString().Trim());
                    entity.Value = declarator.Initializer?.Value.ToString() ?? "";
                    entities.Add(entity);
                }
            }
        }

        return entities;
    }

    static CodeEntity Create(string kind, string name, string file, SyntaxNode node, string code)
    {
        var span = node.GetLocation().GetLineSpan();
        return new CodeEntity
        {
            Kind = kind,
            Name = name,
            File = file,
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            Code = code,
            Node = node,
        };
    }

    static string ContainerName(SyntaxNode node)
    {
        var container = node.Ancestors().FirstOrDefault(a => a is ClassBlockSyntax or ModuleBlockSyntax or StructureBlockSyntax);
        return container is null ? "Global" : QualifiedName((TypeBlockSyntax)container);
    }

    static string QualifiedName(TypeBlockSyntax type)
    {
        var parts = new List<string> { type.BlockStatement.Identifier.Text };
        foreach (var ancestor in type.Ancestors())
        {
            switch (ancestor)
            {
                case TypeBlockSyntax outer:
                    parts.Insert(0, outer.BlockStatement.Identifier.Text);
                    break;
                case NamespaceBlockSyntax ns:
                    parts.Insert(0, ns.NamespaceStatement.Name.ToString());
                    break;
            }
        }
        return string.Join('.', parts);
    }
}
