using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Inertia.Net.Analyzers;

/// <summary>
/// Loader pitfalls the README warns about, found at compile time: a <c>Parallel()</c> or <c>staleFor</c> loader that captures the
/// request's <c>HttpContext</c> or a <c>DbContext</c> (INERTIA003), and <c>Cache(...)</c> on a loader whose value is an
/// <c>InertiaProp</c> (INERTIA004). Only the prop built in the same expression is checked: <c>Prop(...).Parallel()</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoaderAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor CapturedRequestState = new(
        "INERTIA003",
        "Loader captures request state",
        "The {0} loader uses '{1}' ({2}), which belongs to the request; use IDbContextFactory<T>, Prop<TService, T> or a scope of its own",
        "Inertia",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/adzamkomladev/Inertia.Net#parallel-props");

    internal static readonly DiagnosticDescriptor CachedProp = new(
        "INERTIA004",
        "Cached loader returns a prop",
        "Cache(...) needs a plain value, but this loader returns '{0}', an InertiaProp; cache the inner prop instead",
        "Inertia",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/adzamkomladev/Inertia.Net#cached-props");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [CapturedRequestState, CachedProp];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            if (compilation.GetTypeByMetadataName("Inertia.Net.InertiaProp") is not { } propType
                || compilation.GetTypeByMetadataName("Inertia.Net.Inertia") is not { } factories)
            {
                return;
            }

            var httpContext = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpContext");
            var dbContext = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            start.RegisterOperationAction(operation =>
            {
                var invocation = (IInvocationOperation)operation.Operation;
                var method = invocation.TargetMethod;
                if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, propType)) return;

                var staleFor = method.Name == "Cache" && invocation.Arguments.Any(a => a.Parameter?.Name == "staleFor"
                    && a.ArgumentKind != ArgumentKind.DefaultValue && a.Value.ConstantValue is not { HasValue: true, Value: null });
                if (method.Name != "Parallel" && method.Name != "Cache") return;
                if (Factory(invocation.Instance, propType, factories) is not { } factory) return;

                if (method.Name == "Cache" && factory.TargetMethod.TypeArguments.LastOrDefault() is { } valueType && IsOrDerives(valueType, propType))
                {
                    operation.ReportDiagnostic(Diagnostic.Create(CachedProp, invocation.Syntax.GetLocation(), valueType.Name));
                }

                if ((method.Name == "Parallel" || staleFor) && Loader(factory) is { } loader)
                {
                    var kind = method.Name == "Parallel" ? "Parallel()" : "staleFor";
                    foreach (var (reference, name, type) in Captures(loader, httpContext, dbContext))
                    {
                        operation.ReportDiagnostic(Diagnostic.Create(CapturedRequestState, reference.Syntax.GetLocation(), kind, name, type.Name));
                    }
                }
            }, OperationKind.Invocation);
        });
    }

    // Walks a fluent chain (Prop(...).Defer().Parallel()) back to the factory call that created the prop.
    private static IInvocationOperation? Factory(IOperation? instance, INamedTypeSymbol propType, INamedTypeSymbol factories)
    {
        while (instance is not null)
        {
            switch (instance)
            {
                case IConversionOperation conversion:
                    instance = conversion.Operand;
                    continue;
                case IInvocationOperation call when SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, factories):
                    return call;
                case IInvocationOperation call when SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, propType):
                    instance = call.Instance;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private static IAnonymousFunctionOperation? Loader(IInvocationOperation factory)
    {
        foreach (var argument in factory.Arguments)
        {
            var value = argument.Value;
            while (value is IConversionOperation or IDelegateCreationOperation)
            {
                value = value is IConversionOperation conversion ? conversion.Operand : ((IDelegateCreationOperation)value).Target;
            }

            if (value is IAnonymousFunctionOperation lambda) return lambda;
        }

        return null;
    }

    // References to an HttpContext or DbContext declared outside the lambda: captured locals and parameters, fields and properties.
    private static System.Collections.Generic.IEnumerable<(IOperation Reference, string Name, ITypeSymbol Type)> Captures(
        IAnonymousFunctionOperation loader, INamedTypeSymbol? httpContext, INamedTypeSymbol? dbContext)
    {
        var span = loader.Syntax.Span;
        foreach (var descendant in loader.Body.Descendants())
        {
            var (symbol, type) = descendant switch
            {
                ILocalReferenceOperation local => ((ISymbol)local.Local, local.Local.Type),
                IParameterReferenceOperation parameter => (parameter.Parameter, parameter.Parameter.Type),
                IFieldReferenceOperation field => (field.Field, field.Field.Type),
                IPropertyReferenceOperation property => (property.Property, property.Property.Type),
                _ => (null, null),
            };
            if (symbol is null || type is null) continue;
            if (!(IsOrDerives(type, httpContext) || IsOrDerives(type, dbContext))) continue;
            // A property read off a captured value (http.Request.HttpContext) is reported at the captured value itself.
            if (descendant is IPropertyReferenceOperation { Instance: { } owner } && owner is not IInstanceReferenceOperation) continue;
            if (symbol is ILocalSymbol or IParameterSymbol
                && symbol.DeclaringSyntaxReferences.All(r => r.SyntaxTree == loader.Syntax.SyntaxTree && span.Contains(r.Span)))
            {
                continue; // declared in the loader: its own scope, context or lambda parameter
            }

            yield return (descendant, symbol.Name, type);
        }
    }

    private static bool IsOrDerives(ITypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null) return false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
        }

        return false;
    }
}
