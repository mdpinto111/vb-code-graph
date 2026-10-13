using System.Text.RegularExpressions;

namespace VbCodeGraph;

/// <summary>
/// Lightweight JavaScript extraction for the scripts that sit next to WebForms pages.
/// Regex based on purpose: it finds functions and UPPER_SNAKE_CASE constants, not a full AST.
/// </summary>
public static partial class JsParser
{
    [GeneratedRegex(@"^[ \t]*(?:async\s+)?function\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Multiline)]
    private static partial Regex FunctionDeclaration();

    [GeneratedRegex(@"^[ \t]*(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:function\s*\(([^)]*)\)|\(([^)]*)\)\s*=>|(\w+)\s*=>)", RegexOptions.Multiline)]
    private static partial Regex FunctionExpression();

    [GeneratedRegex(@"^[ \t]*(?:const|let|var)\s+([A-Z_][A-Z0-9_]*)\s*=\s*([^;\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex Constant();

    public static List<CodeEntity> Parse(string code, string file)
    {
        var lines = code.Split('\n');
        var entities = new List<CodeEntity>();

        foreach (Match m in FunctionDeclaration().Matches(code))
        {
            entities.Add(Function(code, lines, file, m, m.Groups[1].Value, m.Groups[2].Value));
        }
        foreach (Match m in FunctionExpression().Matches(code))
        {
            var parameters = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            entities.Add(Function(code, lines, file, m, m.Groups[1].Value, parameters));
        }
        foreach (Match m in Constant().Matches(code))
        {
            if (FunctionExpression().IsMatch(m.Value))
            {
                continue;
            }
            var line = LineOf(code, m.Index);
            entities.Add(new CodeEntity
            {
                Kind = EntityKind.Constant,
                Name = $"JS.{m.Groups[1].Value}",
                File = file,
                StartLine = line,
                EndLine = line,
                Code = m.Value.Trim(),
                Value = m.Groups[2].Value.Trim(),
                Language = "JavaScript",
            });
        }
        return entities;
    }

    static CodeEntity Function(string code, string[] lines, string file, Match m, string name, string parameters)
    {
        var start = LineOf(code, m.Index);
        var end = BlockEnd(code, m.Index) ?? start;
        return new CodeEntity
        {
            Kind = EntityKind.Function,
            Name = $"JS.{name}",
            File = file,
            StartLine = start,
            EndLine = end,
            Code = string.Join('\n', lines[(start - 1)..end]).Trim(),
            Parameters = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ReturnsValue = true,
            Language = "JavaScript",
        };
    }

    static int LineOf(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

    /// <summary>Line of the brace that closes the first block opened after <paramref name="start"/>.</summary>
    static int? BlockEnd(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && depth > 0 && --depth == 0)
            {
                return LineOf(code, i);
            }
            else if (depth == 0 && code[i] == ';')
            {
                return LineOf(code, i); // arrow function with an expression body
            }
        }
        return null;
    }
}
