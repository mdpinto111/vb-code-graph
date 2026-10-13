namespace VbCodeGraph.Tests;

public class VbParserTests
{
    const string Source = """
        Namespace Acme.Sales
            Public Class Invoice
                Private Const TAX As Decimal = 0.2D
                Private Shared ReadOnly Prefix As String = "INV-"
                Private _count As Integer = 0

                Public Function Number(id As Integer, year As Integer) As String
                    Return Prefix & year & id
                End Function

                Public Sub Reset()
                    _count = 0
                End Sub

                Public Class Line
                    Public Sub Clear()
                    End Sub
                End Class
            End Class
        End Namespace

        Module Helpers
            Public DefaultCountry As String = "FR"
        End Module
        """;

    readonly List<CodeEntity> entities = VbParser.Parse(Source, "Invoice.vb");

    CodeEntity Get(string name) => entities.Single(e => e.Name == name);

    [Fact]
    public void Qualifies_names_with_namespace_and_enclosing_types()
    {
        Assert.Equal(EntityKind.Class, Get("Acme.Sales.Invoice").Kind);
        Assert.Equal(EntityKind.Class, Get("Acme.Sales.Invoice.Line").Kind);
        Assert.Equal(EntityKind.Sub, Get("Acme.Sales.Invoice.Line.Clear").Kind);
        Assert.Equal(EntityKind.Module, Get("Helpers").Kind);
    }

    [Fact]
    public void Captures_method_signature_and_location()
    {
        var number = Get("Acme.Sales.Invoice.Number");
        Assert.Equal(EntityKind.Function, number.Kind);
        Assert.Equal(["id", "year"], number.Parameters);
        Assert.True(number.ReturnsValue);
        Assert.Equal(7, number.StartLine);
        Assert.Equal(9, number.EndLine);
        Assert.Equal("Invoice.vb", number.File);
        Assert.False(Get("Acme.Sales.Invoice.Reset").ReturnsValue);
    }

    [Fact]
    public void Treats_consts_and_shared_initialized_fields_as_constants()
    {
        Assert.Equal("0.2D", Get("Acme.Sales.Invoice.TAX").Value);
        Assert.Equal("\"INV-\"", Get("Acme.Sales.Invoice.Prefix").Value);
        Assert.Equal("\"FR\"", Get("Helpers.DefaultCountry").Value);
        Assert.DoesNotContain(entities, e => e.Name.EndsWith("_count"));
    }

    [Fact]
    public void Class_code_is_a_summary_without_method_bodies()
    {
        var code = Get("Acme.Sales.Invoice").Code;
        Assert.Contains("Private Const TAX", code);
        Assert.DoesNotContain("Return Prefix", code);
    }
}

public class JsParserTests
{
    const string Source = """
        const MAX_ITEMS = 10;
        const apiBase = "/api";

        function load(page, size) {
            return fetch(apiBase + "?p=" + page);
        }

        const double = (x) => x * 2;
        const greet = function (name) {
            return "hi " + name;
        };
        """;

    readonly List<CodeEntity> entities = JsParser.Parse(Source, "app.js");

    [Fact]
    public void Finds_declarations_arrow_functions_and_function_expressions()
    {
        var functions = entities.Where(e => e.Kind == EntityKind.Function).ToDictionary(e => e.Name);
        Assert.Equal(["page", "size"], functions["JS.load"].Parameters);
        Assert.Equal((4, 6), (functions["JS.load"].StartLine, functions["JS.load"].EndLine));
        Assert.Equal(["x"], functions["JS.double"].Parameters);
        Assert.Equal(8, functions["JS.double"].EndLine);
        Assert.Equal((9, 11), (functions["JS.greet"].StartLine, functions["JS.greet"].EndLine));
        Assert.All(functions.Values, f => Assert.Equal("JavaScript", f.Language));
    }

    [Fact]
    public void Only_upper_snake_case_bindings_are_constants()
    {
        var constant = Assert.Single(entities, e => e.Kind == EntityKind.Constant);
        Assert.Equal("JS.MAX_ITEMS", constant.Name);
        Assert.Equal("10", constant.Value);
    }
}
