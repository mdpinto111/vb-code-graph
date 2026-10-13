namespace VbCodeGraph.Tests;

public class ProjectTests
{
    static readonly string Sample = Path.Combine(AppContext.BaseDirectory, "samples", "LegacyShop");

    [Fact]
    public void Analyzes_the_sample_project()
    {
        var result = ProjectAnalyzer.Analyze(Sample);

        Assert.Equal("LegacyShop", result.Project);
        Assert.Equal(4, result.VbFiles);
        Assert.Equal(1, result.JsFiles);
        Assert.Empty(result.Errors);
        var total = result.All.Single(e => e.Name == "Shop.Billing.PriceCalculator.Total");
        Assert.Equal("App_Code/PriceCalculator.vb", total.File);
        Assert.Equal(
            ["Shop.Billing.PriceCalculator.Subtotal", "Shop.Billing.PriceCalculator.Shipping", "AuditLog.Write", "Settings.FormatPrice"],
            total.Calls);
        Assert.Equal(["Checkout.BindSummary", "OrderService.PlaceOrder"], total.CalledBy.Order());
    }

    [Fact]
    public void Skips_designer_files_unless_asked()
    {
        Assert.Equal(4, ProjectAnalyzer.Analyze(Sample).VbFiles);
        Assert.Equal(5, ProjectAnalyzer.Analyze(Sample, new AnalysisOptions { IncludeDesigner = true }).VbFiles);
        Assert.Equal(0, ProjectAnalyzer.Analyze(Sample, new AnalysisOptions { IncludeJavaScript = false }).JsFiles);
    }

    [Fact]
    public void Skips_build_and_dependency_folders()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var dir in new[] { "bin", "obj", "node_modules", "src" })
            {
                Directory.CreateDirectory(Path.Combine(root, dir));
                File.WriteAllText(Path.Combine(root, dir, "a.vb"), "Module M" + dir.Length + "\nEnd Module");
            }
            File.WriteAllText(Path.Combine(root, "src", "lib.min.js"), "function x(){}");

            var result = ProjectAnalyzer.Analyze(root);
            Assert.Equal(1, result.VbFiles);
            Assert.Equal(0, result.JsFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Json_round_trips()
    {
        var result = ProjectAnalyzer.Analyze(Sample);
        var copy = AnalysisResult.FromJson(result.ToJson());
        Assert.Equal(result.All.Count(), copy.All.Count());
        Assert.Contains("\"calledBy\"", result.ToJson());
    }

    [Fact]
    public void Missing_directory_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => ProjectAnalyzer.Analyze(Path.Combine(Sample, "nope")));
    }
}

public class CallTreeTests
{
    static AnalysisResult Graph(params CodeEntity[] entities) => new()
    {
        Entities = new() { [EntityKind.Sub] = [.. entities] },
    };

    static CodeEntity Sub(string name, params string[] calls) => new() { Kind = EntityKind.Sub, Name = name, Calls = [.. calls] };

    [Fact]
    public void Expands_calls_and_stops_on_recursion()
    {
        var tree = CallTree.Build(Graph(Sub("A.Main", "A.Walk"), Sub("A.Walk", "A.Walk", "A.Missing")), "Main");

        Assert.Equal("A.Main", tree.Name);
        var walk = Assert.Single(tree.Calls);
        Assert.Equal(CallTree.Recursive, walk.Calls[0].Kind);
        Assert.Equal(CallTree.Unknown, walk.Calls[1].Kind);
    }

    [Fact]
    public void Honours_max_depth()
    {
        var tree = CallTree.Build(Graph(Sub("A", "B"), Sub("B", "C"), Sub("C")), "A", maxDepth: 1);
        Assert.Equal("max depth reached", tree.Calls[0].Note);
        Assert.Empty(tree.Calls[0].Calls);
    }

    [Fact]
    public void Rejects_unknown_and_ambiguous_entry_points()
    {
        var graph = Graph(Sub("A.Save"), Sub("B.Save"));
        Assert.Throws<KeyNotFoundException>(() => CallTree.Build(graph, "Load"));
        var ex = Assert.Throws<ArgumentException>(() => CallTree.Build(graph, "Save"));
        Assert.Contains("A.Save", ex.Message);
        Assert.Equal("B.Save", CallTree.Build(graph, "b.save").Name);
    }

    [Fact]
    public void Renders_an_ascii_tree()
    {
        var text = CallTree.Render(CallTree.Build(Graph(Sub("A", "B", "C"), Sub("B"), Sub("C")), "A"));
        Assert.Equal("A\n├── B\n└── C\n", text.ReplaceLineEndings("\n"));
    }
}

public class CliTests
{
    static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Cli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Analyze_then_calltree()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var graph = Path.Combine(dir, "graph.json");
            var tree = Path.Combine(dir, "tree.json");
            var sample = Path.Combine(AppContext.BaseDirectory, "samples", "LegacyShop");

            var analyze = Run("analyze", sample, "-o", graph);
            Assert.Equal(0, analyze.Code);
            Assert.Contains("OrderService.LegacyTotal", analyze.Out);
            Assert.DoesNotContain("Page_Load", analyze.Out);

            var calltree = Run("calltree", graph, "btnConfirm_Click", "-o", tree);
            Assert.Equal(0, calltree.Code);
            Assert.Contains("└── Checkout.CartLines", calltree.Out);
            Assert.True(File.Exists(tree));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(new[] { "analyze" }, 2)]
    [InlineData(new[] { "analyze", ".", "--bogus" }, 2)]
    [InlineData(new[] { "analyze", "does-not-exist" }, 2)]
    [InlineData(new[] { "calltree", "missing.json", "Main" }, 2)]
    [InlineData(new[] { "nope" }, 1)]
    [InlineData(new[] { "--help" }, 0)]
    public void Exit_codes(string[] args, int expected)
    {
        Assert.Equal(expected, Run(args).Code);
    }
}
