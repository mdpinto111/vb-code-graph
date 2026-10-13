# VB Code Graph

[![CI](https://github.com/mdpinto111/vb-code-graph/actions/workflows/ci.yml/badge.svg)](https://github.com/mdpinto111/vb-code-graph/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

`vb-code-graph` maps a legacy **VB.NET / ASP.NET WebForms** code base before you migrate
or refactor it. It parses every `.vb` file with **Roslyn**, plus the JavaScript files that
sit next to the pages, and writes a JSON graph of:

- every **class, module, function, sub and constant**, with its file, lines and code;
- **who calls whom**, in both directions;
- the **classes each method instantiates**;
- the **constants each method reads**, with their values. These are often business
  rules: rates, thresholds and limits.

From that graph it can print the **call tree** of any entry point, such as a button click
handler, and list the methods that nothing calls.

The JSON is meant to be read by a person, by a script, or by any tool that needs a
self-contained slice of the code. A method's code, its callees' code and the constants
they use are all in one place.

## Quick start

```bash
git clone https://github.com/mdpinto111/vb-code-graph.git
cd vb-code-graph
dotnet run --project src/VbCodeGraph -- analyze samples/LegacyShop -o graph.json
```

```text
LegacyShop: 4 VB file(s), 1 JS file(s), 33 entities
  Class         5
  Constant      7
  Function     15
  Module        1
  Sub           5

Most called:
    2  AuditLog.Write
    2  Checkout.CartLines
    2  Settings.FormatPrice
    2  Shop.Billing.PriceCalculator.Total
    1  Checkout.BindSummary

Never called and not an event handler (1):
  OrderService.LegacyTotal  (App_Code/OrderService.vb:30)
```

Then follow everything that one click triggers:

```text
$ dotnet run --project src/VbCodeGraph -- calltree graph.json btnConfirm_Click
Checkout.btnConfirm_Click
├── OrderService.PlaceOrder
│   ├── OrderService.ValidateLines
│   ├── OrderService.LoadDiscount
│   ├── Shop.Billing.PriceCalculator.Total
│   │   ├── Shop.Billing.PriceCalculator.Subtotal
│   │   │   └── Shop.Billing.PriceCalculator.ApplyDiscount
│   │   ├── Shop.Billing.PriceCalculator.Shipping
│   │   ├── AuditLog.Write
│   │   └── Settings.FormatPrice
│   ├── OrderService.SaveOrder
│   └── AuditLog.Write
├── Checkout.CurrentCustomerId
└── Checkout.CartLines
```

[`samples/LegacyShop`](samples/LegacyShop) is a small fictional WebForms checkout used by
the tests and by CI.

## Usage

```
vb-code-graph analyze <project-dir> [-o graph.json] [--include-designer] [--no-js]
vb-code-graph calltree <graph.json> <entry-point> [-o tree.json] [--max-depth N]
```

- **`analyze`** walks the directory and skips `bin`, `obj`, `node_modules`, `packages`
  and minified scripts. Generated `*.designer.vb` files are skipped unless
  `--include-designer` is given.
- **`calltree`** accepts a qualified name (`Checkout.btnConfirm_Click`) or a simple name
  if it is unambiguous. Recursion is detected and marked, not followed. `-o` also writes
  the tree as JSON.

To install it as a global tool:

```bash
dotnet pack src/VbCodeGraph -c Release -o nupkg
dotnet tool install -g vb-code-graph --add-source ./nupkg
```

## Output

Entities are grouped by kind (`Class`, `Module`, `Function`, `Sub`, `Constant`):

```json
{
  "kind": "Function",
  "name": "Shop.Billing.PriceCalculator.Total",
  "file": "App_Code/PriceCalculator.vb",
  "startLine": 26,
  "endLine": 30,
  "code": "Public Function Total(lines As List(Of OrderLine)) As Decimal ...",
  "parameters": ["lines"],
  "returnsValue": true,
  "calls": [
    "Shop.Billing.PriceCalculator.Subtotal",
    "Shop.Billing.PriceCalculator.Shipping",
    "AuditLog.Write",
    "Settings.FormatPrice"
  ],
  "calledBy": ["Checkout.BindSummary", "OrderService.PlaceOrder"],
  "instantiates": [],
  "constantsUsed": { "Settings.VAT_RATE": "0.2D" }
}
```

For a class, `code` is a summary: the declaration, fields and constructors. Each method
is its own entity, so the code is never duplicated.

## How names are resolved

Legacy projects rarely compile outside their original machine because of missing
references, old frameworks and IIS-specific setup. So the tool works on **syntax only**,
without a Roslyn semantic model, and follows VB's own rules as closely as it can:

- Names are **case-insensitive**, as in VB.
- A call resolves first to a method of the **same class or module**, then to an exact
  qualified name, then to the only method with that simple name. When several unrelated
  methods share a name, all of them are linked, so the graph shows too much rather than
  too little.
- **Parameters, locals and `For Each` variables shadow** methods with the same name. A
  variable `total` inside `Subtotal()` is not a call to `Total()`.
- A function that assigns its result to its own name (`Total = ...`) does not call itself.
- **Constants** are `Const` declarations, plus `Shared` or module-level fields with an
  initializer, which old code often uses as constants. Instance fields are state and are
  ignored.
- **JavaScript** is parsed with regular expressions, not a full parser. It finds function
  declarations, function expressions and arrow functions, `UPPER_SNAKE_CASE` constants,
  and calls between those functions.

## Development

```bash
dotnet test
dotnet format --verify-no-changes
```

```
src/VbCodeGraph/
  VbParser.cs         Roslyn syntax walk: entities, signatures, constants
  JsParser.cs         Regex extraction for page scripts
  Linker.cs           Name resolution: calls, called-by, instantiations, constants
  ProjectAnalyzer.cs  File discovery and the JSON model
  CallTree.cs         Transitive call tree with cycle detection
  Program.cs          Command-line interface
tests/VbCodeGraph.Tests/   xUnit tests (parsers, linker, CLI, sample project)
samples/LegacyShop/        Fictional WebForms project
```

CI runs the tests and the sample analysis on Linux and Windows.

## License

[MIT](LICENSE)
