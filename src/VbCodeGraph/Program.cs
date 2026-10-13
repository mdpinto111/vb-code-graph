using System.Text;
using System.Text.Json;
using VbCodeGraph;

Console.OutputEncoding = Encoding.UTF8;
return Cli.Run(args, Console.Out, Console.Error);

public static class Cli
{
    const string Usage = """
        vb-code-graph: map a legacy VB.NET / ASP.NET WebForms code base.

        Usage:
          vb-code-graph analyze <project-dir> [-o graph.json] [--include-designer] [--no-js]
          vb-code-graph calltree <graph.json> <entry-point> [-o tree.json] [--max-depth N]

        analyze   Parses every .vb and .js file and writes classes, modules, methods and
                  constants, with who-calls-whom and the constants each method reads.
        calltree  Prints everything an entry point (e.g. Page_Load) calls, transitively.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "analyze" => Analyze(new Args(args[1..]), stdout),
                "calltree" => Tree(new Args(args[1..]), stdout),
                "-h" or "--help" or "help" => Help(stdout, 0),
                _ => Help(stderr, 1),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or KeyNotFoundException or JsonException
                                       or InvalidDataException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    static int Help(TextWriter writer, int code)
    {
        writer.WriteLine(Usage);
        return code;
    }

    static int Analyze(Args args, TextWriter stdout)
    {
        var project = args.Positional(0, "project directory");
        var output = args.Option("-o") ?? "graph.json";
        var result = ProjectAnalyzer.Analyze(project, new AnalysisOptions
        {
            IncludeDesigner = args.Flag("--include-designer"),
            IncludeJavaScript = !args.Flag("--no-js"),
        });
        File.WriteAllText(output, result.ToJson());

        var all = result.All.ToList();
        stdout.WriteLine($"{result.Project}: {result.VbFiles} VB file(s), {result.JsFiles} JS file(s), {all.Count} entities");
        foreach (var (kind, list) in result.Entities)
        {
            stdout.WriteLine($"  {kind,-9} {list.Count,5}");
        }

        var mostCalled = all.Where(e => e.CalledBy.Count > 0).OrderByDescending(e => e.CalledBy.Count).ThenBy(e => e.Name).Take(5).ToList();
        if (mostCalled.Count > 0)
        {
            stdout.WriteLine("\nMost called:");
            foreach (var e in mostCalled)
            {
                stdout.WriteLine($"  {e.CalledBy.Count,3}  {e.Name}");
            }
        }

        var unused = all.Where(e => EntityKind.IsMethod(e.Kind) && e.CalledBy.Count == 0 && !IsEventHandler(e)).ToList();
        if (unused.Count > 0)
        {
            stdout.WriteLine($"\nNever called and not an event handler ({unused.Count}):");
            foreach (var e in unused.Take(10))
            {
                stdout.WriteLine($"  {e.Name}  ({e.File}:{e.StartLine})");
            }
        }

        foreach (var error in result.Errors)
        {
            stdout.WriteLine($"warning: {error}");
        }
        stdout.WriteLine($"\nWrote {output}");
        return 0;
    }

    static bool IsEventHandler(CodeEntity e) =>
        e.Code.Contains(" Handles ", StringComparison.OrdinalIgnoreCase)
        || (e.Parameters is [_, var args] && args.Equals("e", StringComparison.OrdinalIgnoreCase))
        || e.Language == "JavaScript"; // JS functions are usually wired from markup.

    static int Tree(Args args, TextWriter stdout)
    {
        var analysis = AnalysisResult.FromJson(File.ReadAllText(args.Positional(0, "graph file")));
        var maxDepth = int.TryParse(args.Option("--max-depth"), out var depth) ? depth : 50;
        var tree = CallTree.Build(analysis, args.Positional(1, "entry point"), maxDepth);

        stdout.Write(CallTree.Render(tree));
        if (args.Option("-o") is { } output)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(tree, AnalysisResult.Json));
            stdout.WriteLine($"\nWrote {output}");
        }
        return 0;
    }

    sealed class Args
    {
        readonly List<string> positional = [];
        readonly Dictionary<string, string> options = [];
        readonly HashSet<string> flags = [];
        static readonly HashSet<string> WithValue = ["-o", "--max-depth"];
        static readonly HashSet<string> KnownFlags = ["--include-designer", "--no-js"];

        public Args(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (WithValue.Contains(args[i]))
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{args[i]} needs a value.");
                    }
                    options[args[i]] = args[++i];
                }
                else if (args[i].StartsWith('-'))
                {
                    flags.Add(KnownFlags.Contains(args[i]) ? args[i] : throw new ArgumentException($"Unknown option {args[i]}."));
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
        }

        public string Positional(int index, string what) =>
            index < positional.Count ? positional[index] : throw new ArgumentException($"Missing {what}. Run with --help.");

        public string? Option(string name) => options.GetValueOrDefault(name);

        public bool Flag(string name) => flags.Contains(name);
    }
}
