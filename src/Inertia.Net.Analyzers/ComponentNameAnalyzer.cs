using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Inertia.Net.Analyzers;

/// <summary>
/// Component-name checks at compile time: two page records claiming one component (INERTIA001), and a component name with no
/// page file under <c>$(InertiaPagesDirectory)</c> (INERTIA002, only when that property is set).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentNameAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor DuplicateComponent = new(
        "INERTIA001",
        "Two page types render the same component",
        "Component '{0}' is named by both '{1}' and '{2}'; the generated TypeScript can only describe one of them",
        "Inertia",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor MissingPage = new(
        "INERTIA002",
        "No page file for the component",
        "Component '{0}' has no page file under '{1}'",
        "Inertia",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly string[] Extensions = [".tsx", ".jsx", ".ts", ".js", ".vue", ".svelte"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DuplicateComponent, MissingPage];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(InertiaPagesGenerator.PageInterface) is not { } pageInterface)
            {
                return;
            }

            // The member that implements IInertiaPage.Component is checked against this, found once per compilation.
            var componentMember = pageInterface.GetMembers("Component").FirstOrDefault();
            var pages = Pages(start.Options);
            var claimed = new ConcurrentBag<(string Component, INamedTypeSymbol Type, Location Location)>();

            start.RegisterSyntaxNodeAction(node =>
            {
                var property = (PropertyDeclarationSyntax)node.Node;
                if (property.Identifier.ValueText != "Component"
                    || node.SemanticModel.GetDeclaredSymbol(property, node.CancellationToken) is not { IsStatic: true } symbol
                    || !InertiaPagesGenerator.IsPage(symbol.ContainingType)
                    || ComponentExpression(property) is not { } expression
                    || node.SemanticModel.GetConstantValue(expression, node.CancellationToken) is not { HasValue: true, Value: string component })
                {
                    return;
                }

                // Only the member that implements IInertiaPage.Component (also when implemented explicitly).
                if (componentMember is null || !SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(componentMember), symbol))
                {
                    return;
                }

                claimed.Add((component, symbol.ContainingType, expression.GetLocation()));
                Check(pages, component, expression.GetLocation(), node.ReportDiagnostic);
            }, Microsoft.CodeAnalysis.CSharp.SyntaxKind.PropertyDeclaration);

            if (pages is not null)
            {
                start.RegisterOperationAction(operation =>
                {
                    var invocation = (IInvocationOperation)operation.Operation;
                    if (!IsComponentMethod(invocation.TargetMethod))
                    {
                        return;
                    }

                    var argument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "component");
                    if (argument?.Value.ConstantValue is { HasValue: true, Value: string component })
                    {
                        Check(pages, component, argument.Value.Syntax.GetLocation(), operation.ReportDiagnostic);
                    }
                }, OperationKind.Invocation);
            }

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var group in claimed.GroupBy(c => c.Component, StringComparer.Ordinal))
                {
                    var types = group.OrderBy(c => c.Type.ToDisplayString(), StringComparer.Ordinal).ToList();
                    for (var i = 1; i < types.Count; i++)
                    {
                        end.ReportDiagnostic(Diagnostic.Create(DuplicateComponent, types[i].Location, group.Key, types[0].Type.ToDisplayString(), types[i].Type.ToDisplayString()));
                    }
                }
            });
        });
    }

    // Render(component, props), MapInertia(pattern, component, props) and FastEndpoints' Send.InertiaAsync(component, props).
    private static bool IsComponentMethod(IMethodSymbol method) =>
        method.Name is "Render" or "MapInertia" or "InertiaAsync"
        && method.ContainingType.ToDisplayString() is "Inertia.Net.Inertia" or "Inertia.Net.InertiaEndpointRouteBuilderExtensions" or "Inertia.Net.FastEndpoints.InertiaSendExtensions";

    private static ExpressionSyntax? ComponentExpression(PropertyDeclarationSyntax property) =>
        property.ExpressionBody?.Expression
        ?? property.Initializer?.Value
        ?? property.AccessorList?.Accessors.FirstOrDefault(a => a.Keyword.ValueText == "get") switch
        {
            { ExpressionBody: { } body } => body.Expression,
            { Body.Statements: { Count: 1 } statements } when statements[0] is ReturnStatementSyntax { Expression: { } returned } => returned,
            _ => null,
        };

    private static void Check(PageFiles? pages, string component, Location location, Action<Diagnostic> report)
    {
        if (pages is not null && !pages.Components.Contains(component))
        {
            report(Diagnostic.Create(MissingPage, location, component, pages.Directory));
        }
    }

    // The page files the build passes as AdditionalFiles (buildTransitive/Inertia.Net.props, when InertiaPagesDirectory is set).
    private static PageFiles? Pages(AnalyzerOptions options)
    {
        if (!options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.InertiaPagesDirectory", out var directory)
            || string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var marker = "/" + directory.Replace('\\', '/').Trim('/') + "/";
        var components = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in options.AdditionalFiles.Select(file => file.Path.Replace('\\', '/')))
        {
            var at = path.LastIndexOf(marker, StringComparison.Ordinal);
            var extension = Path.GetExtension(path);
            if (at >= 0 && Extensions.Contains(extension, StringComparer.Ordinal))
            {
                var relative = path.Substring(at + marker.Length);
                components.Add(relative.Substring(0, relative.Length - extension.Length));
            }
        }

        return new PageFiles(directory, components);
    }

    private sealed class PageFiles(string directory, HashSet<string> components)
    {
        public string Directory { get; } = directory;

        public HashSet<string> Components { get; } = components;
    }
}
