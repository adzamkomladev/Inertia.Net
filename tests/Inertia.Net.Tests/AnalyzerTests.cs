using System.Collections.Immutable;
using Inertia.Net.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Inertia.Net.Tests;

/// <summary>The source generator (<c>AddGeneratedPages</c>) and the component-name analyzer, run on in-memory compilations.</summary>
public sealed class AnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => !Path.GetFileName(p).StartsWith("Inertia.Net", StringComparison.Ordinal))
            .Select(p => MetadataReference.CreateFromFile(p)),
        MetadataReference.CreateFromFile(typeof(IInertiaPage).Assembly.Location),
    ];

    private const string Usings = "using Inertia.Net;\nusing static Inertia.Net.Inertia;\n";

    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create(
        "App",
        [CSharpSyntaxTree.ParseText(Usings + source, CSharpParseOptions.Default)],
        References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    private static (string Source, ImmutableArray<Diagnostic> Diagnostics) Generate(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new InertiaPagesGenerator()).RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out _, TestContext.Current.CancellationToken);
        var generated = driver.GetRunResult().GeneratedTrees.SingleOrDefault()?.ToString() ?? "";
        return (generated, output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray());
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, string? pagesDirectory = null, params string[] pageFiles)
    {
        var options = new AnalyzerOptions(
            [.. pageFiles.Select(f => (AdditionalText)new Page(f))],
            new ConfigOptions(pagesDirectory is null ? [] : new() { ["build_property.InertiaPagesDirectory"] = pagesDirectory }));
        var compilation = Compile(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers([new ComponentNameAnalyzer()], options).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_generator_registers_every_reachable_page_type_in_order()
    {
        var (generated, errors) = Generate("""
            namespace App;
            public sealed record B(int X) : IInertiaPage { public static string Component => "B"; }
            internal sealed record A(int X) : IInertiaPage { static string IInertiaPage.Component => "A"; }
            public abstract record Base : IInertiaPage { public static string Component => "Base"; }
            public sealed record Derived : Base;
            public static class Outer
            {
                public sealed record Nested : IInertiaPage { public static string Component => "Nested"; }
                private sealed record Hidden : IInertiaPage { public static string Component => "Hidden"; }
            }
            public sealed record Generic<T> : IInertiaPage { public static string Component => "G"; }
            public sealed record Lookalike : Fake.IInertiaPage, Inertia.Net.IInertiaPage;
            public static class Fake { public interface IInertiaPage { } }
            public static class Inertia { public static class Net { public interface IInertiaPage { } } }
            public static class Use
            {
                public static InertiaTypeScriptOptions Run(InertiaTypeScriptOptions o) => o.AddGeneratedPages();
            }
            """);
        Assert.Empty(errors);
        Assert.Contains("""
                        options.Page<global::App.A>();
                        options.Page<global::App.B>();
                        options.Page<global::App.Derived>();
                        options.Page<global::App.Outer.Nested>();
                        return options;
            """.ReplaceLineEndings("\n"), generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Generic", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Base>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Lookalike", generated, StringComparison.Ordinal); // same name, another namespace
    }

    [Fact]
    public void The_generator_emits_nothing_without_a_reference_to_Inertia_Net()
    {
        var driver = CSharpGeneratorDriver.Create(new InertiaPagesGenerator()).RunGenerators(CSharpCompilation.Create(
            "Plain", [CSharpSyntaxTree.ParseText("public record A(int X);", cancellationToken: TestContext.Current.CancellationToken)], References.Take(References.Length - 1)),
            TestContext.Current.CancellationToken);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
    }

    [Fact]
    public async Task Two_page_types_claiming_one_component_are_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record A(int X) : IInertiaPage { public static string Component => "Users/Index"; }
            public sealed record B(int X) : IInertiaPage { static string IInertiaPage.Component { get { return "Users/Index"; } } }
            public sealed record C(int X) : IInertiaPage { public static string Component { get; } = "Other"; }
            """);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("INERTIA001", diagnostic.Id);
        Assert.Contains("'Users/Index' is named by both 'A' and 'B'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Component_names_without_a_page_file_are_reported_only_when_the_pages_directory_is_set()
    {
        const string source = """
            public sealed record A(int X) : IInertiaPage { public static string Component => "Users/Missing"; }
            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
                {
                    app.MapInertia("/", "Home");
                    app.MapInertia("/about", "About");
                }

                public static InertiaResult Show() => Render("Users/Index", null);
                public static InertiaResult Typo() => Render("Users/Indx");
                public static InertiaResult Dynamic(string name) => Render(name);
                public static InertiaResult Typed() => Render(new A(1));
            }
            """;
        Assert.Empty(await AnalyzeAsync(source));

        var diagnostics = await AnalyzeAsync(source, "ClientApp/pages", "/app/ClientApp/pages/Home.vue", "/app/ClientApp/pages/Users/Index.tsx", "/app/ClientApp/pages/About.md");
        Assert.All(diagnostics, d => Assert.Equal("INERTIA002", d.Id));
        Assert.Equal(
            ["'About'", "'Users/Indx'", "'Users/Missing'"],
            diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Split(' ')[1]).Order(StringComparer.Ordinal));
    }

    private sealed class Page(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From("");
    }

    private sealed class ConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options([]);

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
