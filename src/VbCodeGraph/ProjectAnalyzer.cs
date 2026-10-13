using System.Text.Json;
using System.Text.Json.Serialization;

namespace VbCodeGraph;

public sealed class AnalysisOptions
{
    public bool IncludeDesigner { get; init; }
    public bool IncludeJavaScript { get; init; } = true;
}

public sealed class AnalysisResult
{
    public string Project { get; init; } = "";
    public DateTimeOffset AnalyzedAt { get; init; }
    public int VbFiles { get; init; }
    public int JsFiles { get; init; }
    public List<string> Errors { get; init; } = [];
    public SortedDictionary<string, List<CodeEntity>> Entities { get; init; } = [];

    [JsonIgnore]
    public IEnumerable<CodeEntity> All => Entities.Values.SelectMany(e => e);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep quotes, <, > and non-Latin text readable in the code snippets.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AnalysisResult FromJson(string json) =>
        JsonSerializer.Deserialize<AnalysisResult>(json, Json) ?? throw new InvalidDataException("Empty analysis file.");
}

public static class ProjectAnalyzer
{
    static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", ".git", ".vs",
    };

    public static AnalysisResult Analyze(string projectPath, AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions();
        var root = Path.GetFullPath(projectPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Directory not found: {projectPath}");
        }

        var files = SourceFiles(root, options).ToList();
        var entities = new List<CodeEntity>();
        var errors = new List<string>();

        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            try
            {
                var code = File.ReadAllText(path);
                entities.AddRange(IsVb(path) ? VbParser.Parse(code, relative) : JsParser.Parse(code, relative));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{relative}: {ex.Message}");
            }
        }

        Linker.Link(entities);

        return new AnalysisResult
        {
            Project = Path.GetFileName(root),
            AnalyzedAt = DateTimeOffset.Now,
            VbFiles = files.Count(IsVb),
            JsFiles = files.Count(f => !IsVb(f)),
            Errors = errors,
            Entities = new SortedDictionary<string, List<CodeEntity>>(
                entities.GroupBy(e => e.Kind).ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.StartLine).ToList())),
        };
    }

    static bool IsVb(string path) => path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> SourceFiles(string root, AnalysisOptions options)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
            foreach (var file in Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (IsVb(name))
                {
                    if (options.IncludeDesigner || !name.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return file;
                    }
                }
                else if (options.IncludeJavaScript
                         && name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                         && !name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}
