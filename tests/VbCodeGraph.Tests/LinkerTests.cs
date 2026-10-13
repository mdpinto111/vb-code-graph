namespace VbCodeGraph.Tests;

public class LinkerTests
{
    static Dictionary<string, CodeEntity> Link(params (string File, string Code)[] files)
    {
        var entities = files
            .SelectMany(f => f.File.EndsWith(".js") ? JsParser.Parse(f.Code, f.File) : VbParser.Parse(f.Code, f.File))
            .ToList();
        Linker.Link(entities);
        return entities.ToDictionary(e => e.Name);
    }

    [Fact]
    public void Prefers_a_method_of_the_same_class()
    {
        var graph = Link(("a.vb", """
            Class Cart
                Sub Save()
                    Validate()
                End Sub
                Sub Validate()
                End Sub
            End Class
            Class Order
                Sub Validate()
                End Sub
            End Class
            """));

        Assert.Equal(["Cart.Validate"], graph["Cart.Save"].Calls);
        Assert.Equal(["Cart.Save"], graph["Cart.Validate"].CalledBy);
        Assert.Empty(graph["Order.Validate"].CalledBy);
    }

    [Fact]
    public void Resolves_calls_across_files_case_insensitively()
    {
        var graph = Link(
            ("page.vb", """
                Class Page
                    Sub Load()
                        Dim s As New Pricing()
                        lbl.Text = s.computetotal(3)
                    End Sub
                End Class
                """),
            ("pricing.vb", """
                Class Pricing
                    Function ComputeTotal(n As Integer) As Integer
                        Return n
                    End Function
                End Class
                """));

        Assert.Equal(["Pricing.ComputeTotal"], graph["Page.Load"].Calls);
        Assert.Equal(["Pricing"], graph["Page.Load"].Instantiates);
    }

    [Fact]
    public void Locals_and_parameters_shadow_methods_with_the_same_name()
    {
        var graph = Link(("a.vb", """
            Class Calc
                Function Total() As Integer
                    Total = Subtotal(1)
                End Function
                Function Subtotal(total As Integer) As Integer
                    Dim shipping = 2
                    For Each item In New Integer() {1}
                    Next
                    Return total + shipping
                End Function
                Function Shipping() As Integer
                    Return 0
                End Function
                Function Item() As Integer
                    Return 0
                End Function
            End Class
            """));

        Assert.Equal(["Calc.Subtotal"], graph["Calc.Total"].Calls);
        Assert.Empty(graph["Calc.Subtotal"].Calls);
    }

    [Fact]
    public void Tracks_constants_by_simple_and_qualified_name()
    {
        var graph = Link(("a.vb", """
            Module Config
                Public Const RATE As Decimal = 0.2D
                Public Const LIMIT As Integer = 5
            End Module
            Class Tax
                Private Const LIMIT As Integer = 9
                Function Apply(x As Decimal) As Decimal
                    If x > LIMIT Then Return x
                    Return x * Config.RATE
                End Function
            End Class
            """));

        var used = graph["Tax.Apply"].ConstantsUsed;
        Assert.Equal("0.2D", used["Config.RATE"]);
        Assert.Equal("9", used["Tax.LIMIT"]);
        Assert.False(used.ContainsKey("Config.LIMIT"));
    }

    [Fact]
    public void Links_constructor_calls_to_the_class()
    {
        var graph = Link(("a.vb", """
            Class Repo
                Private ReadOnly _db As New Connection()
                Sub New()
                    Open()
                End Sub
                Sub Open()
                End Sub
            End Class
            Class Connection
            End Class
            """));

        Assert.Equal(["Repo.Open"], graph["Repo"].Calls);
        Assert.Equal(["Connection"], graph["Repo"].Instantiates);
    }

    [Fact]
    public void Links_javascript_calls_and_constants()
    {
        var graph = Link(("a.js", """
            const LIMIT = 3;
            function run(n) {
                return helper(Math.min(n, LIMIT));
            }
            function helper(x) {
                return x;
            }
            """));

        Assert.Equal(["JS.helper"], graph["JS.run"].Calls);
        Assert.Equal(["JS.run"], graph["JS.helper"].CalledBy);
        Assert.Equal("3", graph["JS.run"].ConstantsUsed["JS.LIMIT"]);
        Assert.Empty(graph["JS.helper"].Calls);
    }
}
