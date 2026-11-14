using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>
/// TypeScript declarations for the client (<see cref="InertiaOptions.TypeScript"/>): one interface per registered page, the shared props and
/// flash data, and the <c>InertiaConfig</c> augmentation of <c>@inertiajs/core</c>. Types follow the app's JSON contracts (the HTTP JSON
/// options and its source-generated contexts), so property names, nullability and enum values are what the page JSON holds.
/// </summary>
public sealed class InertiaTypeScriptOptions
{
    internal List<(string Component, Type Props)> Pages { get; } = [];

    internal Type? SharedPropsType { get; private set; }

    internal Type? FlashDataType { get; private set; }

    internal List<LiveEvent> LiveEventList { get; } = [];

    /// <summary>
    /// The file to write, relative to the content root, e.g. <c>ClientApp/types/inertia.d.ts</c>. It is written when the app starts in
    /// Development, and only when its content changed. Null (the default) writes nothing. Run the app with <c>--inertia-types</c>
    /// (<c>dotnet run -- --inertia-types</c>) to write it in any environment and exit, e.g. in CI.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// The <c>routes.ts</c> file to write, relative to the content root, e.g. <c>ClientApp/types/routes.ts</c>: one function per named
    /// endpoint (<c>WithName("todos.show")</c> becomes <c>routes.todos.show(1)</c>, returning <c>{ url, method }</c>), written like
    /// <see cref="OutputPath"/>. Unnamed endpoints are skipped. Null (the default) writes nothing.
    /// </summary>
    public string? RoutesOutputPath { get; set; }

    /// <summary>Declares that <paramref name="component"/> renders with props of type <typeparamref name="TProps"/>, listed in <c>InertiaPages</c>.</summary>
    /// <typeparam name="TProps">The typed props object passed to <c>Render</c>; register it in the app's JSON context under Native AOT.</typeparam>
    public InertiaTypeScriptOptions Page<TProps>(string component)
    {
        ArgumentException.ThrowIfNullOrEmpty(component);
        AddPage(component, typeof(TProps));
        return this;
    }

    /// <summary>Declares the page <typeparamref name="TPage"/> under the component it names (<see cref="IInertiaPage.Component"/>).</summary>
    public InertiaTypeScriptOptions Page<TPage>() where TPage : IInertiaPage => Page<TPage>(TPage.Component);

    /// <summary>
    /// Declares every public <see cref="IInertiaPage"/> type in <paramref name="assembly"/>, as <see cref="Page{TPage}()"/> does for one.
    /// It uses reflection; under trimming, register pages one by one with <see cref="Page{TPage}()"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Scans the assembly for IInertiaPage types; use Page<TPage>() when trimming.")]
    public InertiaTypeScriptOptions PagesFrom(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(IInertiaPage).IsAssignableFrom(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            // The interface map finds the Component getter also when the type implements it explicitly.
            var map = type.GetInterfaceMap(typeof(IInertiaPage));
            AddPage((string)map.TargetMethods[0].Invoke(null, null)!, type);
        }

        return this;
    }

    private void AddPage(string component, Type props)
    {
        if (!Pages.Contains((component, props)))
        {
            Pages.Add((component, props));
        }
    }

    /// <summary>
    /// Types the shared props with <typeparamref name="TShared"/>. Without it, <c>SharedProps</c> is inferred from the values and typed
    /// factories given to <see cref="InertiaOptions.Share(string, object?)"/>; shares added per request (middleware,
    /// <c>HttpContext.Inertia().Share</c>) are not known at startup, so type them with this.
    /// </summary>
    public InertiaTypeScriptOptions SharedProps<TShared>()
    {
        SharedPropsType = typeof(TShared);
        return this;
    }

    /// <summary>
    /// Exports <paramref name="events"/> from <see cref="RoutesOutputPath"/> as <c>liveEvents</c>, keyed by event name
    /// (<c>liveEvents.OrderUpdated</c> is <c>{ name, channel: { type, name } }</c>), so the client subscribes without retyping them.
    /// </summary>
    public InertiaTypeScriptOptions LiveEvents(params LiveEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (!LiveEventList.Contains(e)) LiveEventList.Add(e);
        }

        return this;
    }

    /// <summary>
    /// Types the whole flash object (<c>page.flash</c>) with <typeparamref name="TFlash"/>: its properties are the flash keys. For
    /// <c>Flash("toast", ...)</c>, use <c>record Flash(Toast? Toast)</c>, then <c>page.flash.toast</c> is typed.
    /// </summary>
    public InertiaTypeScriptOptions FlashData<TFlash>()
    {
        FlashDataType = typeof(TFlash);
        return this;
    }
}

/// <summary>
/// Names the value type of an <see cref="InertiaProp"/> member of a typed page, for the TypeScript declarations
/// (<see cref="InertiaOptions.TypeScript"/>): <c>[property: InertiaPropType(typeof(Stats))] InertiaProp Stats</c> becomes
/// <c>stats?: Stats</c> instead of <c>stats?: unknown</c>. It has no effect on rendering.
/// </summary>
/// <param name="valueType">The type the prop's value or loader produces.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class InertiaPropTypeAttribute(Type valueType) : Attribute
{
    /// <summary>The type the prop's value or loader produces.</summary>
    public Type ValueType { get; } = valueType;
}

/// <summary>
/// Writes the declarations at startup in Development (no-op otherwise). With <c>--inertia-types</c> on the command line it writes them in
/// any environment and stops the app, failing the exit code when they cannot be written.
/// </summary>
internal sealed partial class InertiaTypeScriptWriter(
    IOptions<InertiaOptions> options,
    IHostEnvironment environment,
    InertiaPageWriter pageWriter,
    ILogger<InertiaTypeScriptWriter> logger,
    IHostApplicationLifetime? lifetime = null,
    IServiceProvider? services = null) : IHostedService
{
    internal const string GenerateFlag = "--inertia-types";

    /// <summary>True when the process was started with <c>--inertia-types</c>.</summary>
    internal static bool IsGenerateOnly { get; } = Environment.GetCommandLineArgs().Contains(GenerateFlag, StringComparer.Ordinal);

    internal bool GenerateOnly { get; init; } = IsGenerateOnly;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!GenerateOnly && !environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        var typescript = options.Value.TypeScript;
        // Routes come from the app's endpoints, which all mounts share; the default mount writes them.
        var routes = options.Value.Mount is null ? typescript.RoutesOutputPath : null;
        var written = true;
        if (GenerateOnly && typescript.OutputPath is not { Length: > 0 } && routes is not { Length: > 0 })
        {
            LogNoOutputPath(logger);
            written = false;
        }

        if (typescript.OutputPath is { Length: > 0 } output)
        {
            written &= WriteFile(output, warnings =>
            {
                var generator = new TypeScriptGenerator(pageWriter.SerializerOptions, options.Value);
                var content = generator.Generate();
                warnings.AddRange(generator.Warnings);
                return content;
            });
        }

        // Endpoints are complete only once the server has built its pipeline, which happens after the app's hosted services start
        // (WebApplication registers the web host last). Stopping inside StartAsync would also make the host throw.
        // The routes file is written even when the declarations file was not.
        if (lifetime is null)
        {
            var routesWritten = WriteRoutes(routes);
            Finish(written && routesWritten);
        }
        else
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                var routesWritten = WriteRoutes(routes);
                Finish(written && routesWritten);
            });
        }

        return Task.CompletedTask;
    }

    private void Finish(bool written)
    {
        if (GenerateOnly)
        {
            if (!written)
            {
                Environment.ExitCode = 1;
            }

            lifetime?.StopApplication();
        }
    }

    private bool WriteRoutes(string? routes) => routes is not { Length: > 0 } || WriteFile(routes, warnings =>
    {
        var generator = new RoutesGenerator(new TypeScriptGenerator(pageWriter.SerializerOptions, options.Value, RoutesGenerator.ReservedNames),
            services?.GetServices<IRouteBodySource>() ?? [], options.Value.TypeScript.LiveEventList);
        var content = generator.Generate(services?.GetService<EndpointDataSource>()?.Endpoints ?? []);
        warnings.AddRange(generator.Warnings);
        if (generator.Unnamed > 0)
        {
            LogUnnamed(logger, generator.Unnamed);
        }

        return content;
    });

    private bool WriteFile(string output, Func<List<string>, string> generate)
    {
        var path = Path.GetFullPath(output, environment.ContentRootPath); // an absolute path is used as is
        try
        {
            var warnings = new List<string>();
            var content = generate(warnings);
            foreach (var warning in warnings)
            {
                LogWarning(logger, warning);
            }

            // Unchanged content is not rewritten, so the Vite dev server does not reload for nothing.
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
                LogWritten(logger, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            // A development convenience must not stop the app from starting.
            LogFailed(logger, ex, path);
            return false;
        }

        return true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Inertia TypeScript declarations written to {Path}.")]
    private static partial void LogWritten(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia TypeScript declarations: {Warning}")]
    private static partial void LogWarning(ILogger logger, string warning);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inertia routes: {Count} unnamed endpoints were skipped; name an endpoint (WithName) to generate its route.")]
    private static partial void LogUnnamed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "--inertia-types was given but neither InertiaOptions.TypeScript.OutputPath nor RoutesOutputPath is set; nothing was written.")]
    private static partial void LogNoOutputPath(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inertia TypeScript declarations could not be written to {Path}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string path);
}

/// <summary>Maps JSON contracts to TypeScript declarations.</summary>
internal sealed class TypeScriptGenerator(JsonSerializerOptions serializerOptions, InertiaOptions options, IEnumerable<string>? reservedNames = null)
{
    private readonly Dictionary<Type, string> _names = [];
    private readonly HashSet<string> _usedNames = new(reservedNames ?? ["InertiaPages", "SharedProps", "FlashData"], StringComparer.Ordinal);
    private readonly Queue<Type> _pending = new();
    private readonly StringBuilder _declarations = new();

    public List<string> Warnings { get; } = [];

    public string Generate()
    {
        var typescript = options.TypeScript;
        var head = new StringBuilder();
        head.Append("// Generated by Inertia.Net from the app's JSON contracts. Do not edit: it is rewritten when the app starts in Development\n// or runs with --inertia-types.\n\n");

        head.Append("export interface InertiaPages {\n");
        foreach (var (component, props) in typescript.Pages)
        {
            head.Append("  ").Append(Key(component)).Append(": ").Append(Reference(props)).Append('\n');
        }

        head.Append("}\n\n");

        head.Append("export interface SharedProps ");
        if (typescript.SharedPropsType is { } shared)
        {
            head.Append("extends ").Append(Reference(shared)).Append(" {\n  ").Append(Key(options.Head.PropName)).Append("?: string[]\n}\n\n");
        }
        else
        {
            head.Append(InferSharedProps()).Append("\n\n");
        }

        if (typescript.FlashDataType is { } flash)
        {
            head.Append("export interface FlashData extends ").Append(Reference(flash)).Append(" {}\n\n");
        }

        head.Append(Declarations());
        head.Append("declare module '@inertiajs/core' {\n  export interface InertiaConfig {\n    sharedPageProps: SharedProps\n");
        if (typescript.FlashDataType is not null)
        {
            head.Append("    flashDataType: FlashData\n");
        }

        head.Append("    errorValueType: ").Append(options.WithAllErrors ? "string[]" : "string").Append("\n  }\n}\n");
        return head.ToString();
    }

    /// <summary>The declarations of the named types referenced so far, then of the types those reference, and so on.</summary>
    internal string Declarations()
    {
        while (_pending.TryDequeue(out var type))
        {
            Declare(type);
        }

        var text = _declarations.ToString();
        _declarations.Clear();
        return text;
    }

    // The shared props given to InertiaOptions.Share, with dot keys nested like the page JSON nests them.
    private string InferSharedProps()
    {
        var root = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in options.SharedProps)
        {
            var segments = key.Split('.');
            var node = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!node.TryGetValue(segments[i], out var child) || child is not SortedDictionary<string, object> nested)
                {
                    node[segments[i]] = nested = new SortedDictionary<string, object>(StringComparer.Ordinal);
                }

                node = nested;
            }

            node[segments[^1]] = value is InertiaProp { Load: not PropLoad.Eager } prop
                ? (Optional: true, Type: ValueReference(prop))
                : (Optional: false, Type: value switch
                {
                    null => "null",
                    InertiaProp prop2 => ValueReference(prop2),
                    Func<HttpContext, object?> => options.SharedPropTypes.TryGetValue(key, out var type) ? Reference(type) : "unknown",
                    _ => Reference(value.GetType()),
                });
        }

        root.TryAdd(options.Head.PropName, (Optional: true, Type: "string[]")); // InertiaHead tags, sent when a page sets any
        return Literal(root, "");

        string Literal(SortedDictionary<string, object> node, string indent)
        {
            var text = new StringBuilder("{\n");
            foreach (var (name, entry) in node)
            {
                text.Append(indent).Append("  ").Append(Key(name));
                if (entry is SortedDictionary<string, object> nested)
                {
                    text.Append(": ").Append(Literal(nested, indent + "  "));
                }
                else
                {
                    var (optional, type) = ((bool, string))entry;
                    text.Append(optional ? "?: " : ": ").Append(type);
                }

                text.Append('\n');
            }

            return text.Append(indent).Append('}').ToString();
        }

        string ValueReference(InertiaProp prop) => prop.ValueType == typeof(object) ? "unknown" : Reference(prop.ValueType);
    }

    /// <summary>The TypeScript type of a value of <paramref name="type"/>; named types are queued for declaration.</summary>
    internal string Reference(Type type, JsonNumberHandling? handling = null)
    {
        if (typeof(InertiaProp).IsAssignableFrom(type) || type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonNode) || type == typeof(JsonValue))
        {
            return "unknown";
        }

        JsonTypeInfo info;
        try
        {
            info = serializerOptions.GetTypeInfo(type);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            Warnings.Add($"'{type}' has no JSON metadata (register it in the app's JsonSerializerContext); it is typed as unknown.");
            return "unknown";
        }

        var converter = info.Converter.GetType();
        if (converter.Assembly != typeof(JsonSerializer).Assembly && converter.DeclaringType != typeof(BigIntegerConverters))
        {
            return UnknownConverter(type);
        }

        handling ??= info.NumberHandling ?? serializerOptions.NumberHandling;

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Union(Reference(underlying, handling), "null");
        }

        if (type == typeof(JsonObject))
        {
            return "Record<string, unknown>";
        }

        if (type == typeof(JsonArray))
        {
            return "unknown[]";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)
            || type == typeof(Version) || type == typeof(byte[]))
        {
            return "string";
        }

        if (type == typeof(long) || type == typeof(ulong) || type == typeof(Int128) || type == typeof(UInt128) || type == typeof(BigInteger))
        {
            // Beyond 2^53 the client revives these as BigInt (PreserveBigIntegers).
            return Number(options.PreserveBigIntegers ? "number | bigint" : "number", handling.Value);
        }

        if (type.IsPrimitive || type == typeof(decimal) || type == typeof(Half))
        {
            return Number("number", handling.Value);
        }

        if (type.IsEnum)
        {
            return Named(type);
        }

        return info.Kind switch
        {
            JsonTypeInfoKind.Enumerable => Array(Reference(info.ElementType ?? typeof(object), handling)),
            JsonTypeInfoKind.Dictionary => $"Record<string, {Reference(info.ElementType ?? typeof(object), handling)}>",
            JsonTypeInfoKind.Object => Named(type),
#if NET11_0_OR_GREATER
            JsonTypeInfoKind.Union => UnionCases(info, handling),
#endif
            _ => "unknown", // a custom converter: its output shape is unknown
        };
    }

#if NET11_0_OR_GREATER
    // A C# union is written as its active case, with no wrapper or discriminator (a default struct union writes null; not modeled).
    private string UnionCases(JsonTypeInfo info, JsonNumberHandling? handling)
    {
        var type = info.UnionCases.Select(c => Reference(c.CaseType, handling)).Aggregate(Union);
        return info.UnionCases.Any(c => c.IsNullable) ? Union(type, "null") : type;
    }
#endif

    private static string Number(string type, JsonNumberHandling handling) => (handling & JsonNumberHandling.WriteAsString) != 0 ? Union(type, "string") : type;

    private string UnknownConverter(Type type)
    {
        Warnings.Add($"'{type}' uses a custom JSON converter whose output shape is unknown; it is typed as unknown.");
        return "unknown";
    }

    private string Named(Type type)
    {
        if (!_names.TryGetValue(type, out var name))
        {
            name = UniqueName(type);
            _names[type] = name;
            _pending.Enqueue(type);
        }

        return name;
    }

    private void Declare(Type type)
    {
        var name = _names[type];
        if (type.IsEnum)
        {
            var (values, names) = EnumValues(type);
            if (names is not null)
            {
                _declarations.Append("/** ").Append(names).Append(" */\n"); // numeric values: name them in the IDE tooltip
            }

            _declarations.Append("export type ").Append(name).Append(" = ").Append(values).Append("\n\n");
            return;
        }

        var info = serializerOptions.GetTypeInfo(type);
        if (info.PolymorphismOptions is { DerivedTypes.Count: > 0 } polymorphism)
        {
            // The base is written as one of its derived types, each tagged with its discriminator.
            var cases = new List<string>();
            foreach (var derived in polymorphism.DerivedTypes)
            {
                if (_names.TryGetValue(derived.DerivedType, out var known))
                {
                    cases.Add(known); // also used on its own, declared from the queue
                    continue;
                }

                var caseName = UniqueName(derived.DerivedType);
                _names[derived.DerivedType] = caseName;
                var discriminator = derived.TypeDiscriminator switch
                {
                    string text => Quote(text),
                    int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => null,
                };
                Interface(caseName, serializerOptions.GetTypeInfo(derived.DerivedType), discriminator is null ? null : (polymorphism.TypeDiscriminatorPropertyName, discriminator));
                cases.Add(caseName);
            }

            if (!type.IsAbstract)
            {
                var baseName = UniqueName(type, "Base");
                Interface(baseName, info, null);
                cases.Add(baseName);
            }

            _declarations.Append("export type ").Append(name).Append(" = ").Append(string.Join(" | ", cases)).Append("\n\n");
            return;
        }

        Interface(name, info, null);
    }

    private void Interface(string name, JsonTypeInfo info, (string Name, string Value)? discriminator)
    {
        var ignore = serializerOptions.DefaultIgnoreCondition;
        var body = new StringBuilder();
        if (discriminator is { } tag)
        {
            body.Append("  ").Append(Key(tag.Name)).Append(": ").Append(tag.Value).Append('\n');
        }

        foreach (var property in info.Properties)
        {
            if (property.Get is null)
            {
                continue;
            }

            if (property.IsExtensionData)
            {
                body.Append("  [key: string]: unknown\n");
                continue;
            }

            var declared = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var nullable = property.IsGetNullable && !typeof(InertiaProp).IsAssignableFrom(declared);
            var type = property.CustomConverter is not null ? UnknownConverter(declared)
                : typeof(InertiaProp).IsAssignableFrom(declared) && PropValueType(property) is { } valueType ? Reference(valueType)
                : Reference(property.PropertyType, property.NumberHandling ?? info.NumberHandling);
            // A prop member can be left out (optional, deferred, rescued); so can members that a condition skips.
            var optional = typeof(InertiaProp).IsAssignableFrom(declared)
                || property.ShouldSerialize is not null
                || ignore == JsonIgnoreCondition.WhenWritingDefault
                || (ignore == JsonIgnoreCondition.WhenWritingNull && nullable);
            body.Append("  ").Append(Key(property.Name)).Append(optional ? "?: " : ": ").Append(nullable ? Union(type, "null") : type).Append('\n');
        }

        _declarations.Append("export interface ").Append(name).Append(" {\n").Append(body).Append("}\n\n");
    }

    private static Type? PropValueType(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(InertiaPropTypeAttribute), inherit: false) is [InertiaPropTypeAttribute attribute, ..]
            ? attribute.ValueType
            : null;

    // The values the serializer writes for each member: strings with a string enum converter, numbers otherwise. Numeric values also
    // return their member names (Low = 0, High = 1) for a doc comment.
    private (string Values, string? Names) EnumValues(Type type)
    {
        var info = serializerOptions.GetTypeInfo(type);
        if (type.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return (JsonSerializer.Serialize(Enum.ToObject(type, 0), info).StartsWith('"') ? "string" : "number", null);
        }

        var values = new List<string>();
        var names = new List<string>();
        foreach (var value in Enum.GetValuesAsUnderlyingType(type))
        {
            var member = Enum.ToObject(type, value!);
            var json = JsonSerializer.Serialize(member, info);
            var numeric = !json.StartsWith('"');
            var literal = numeric ? json : Quote(JsonSerializer.Deserialize(json, InertiaJsonContext.Default.String)!);
            if (numeric)
            {
                names.Add($"{Enum.GetName(type, member)} = {json}");
            }

            if (!values.Contains(literal))
            {
                values.Add(literal);
            }
        }

        return (values.Count == 0 ? "never" : string.Join(" | ", values), names.Count == 0 ? null : string.Join(", ", names));
    }

    private string UniqueName(Type type, string suffix = "")
    {
        var name = Identifier(type) + suffix;
        var candidate = name;
        for (var i = 2; !_usedNames.Add(candidate); i++)
        {
            candidate = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    // Paged<User> becomes PagedUser; nested types keep only their own name.
    private static string Identifier(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (type.IsGenericType)
        {
            name += string.Concat(type.GetGenericArguments().Select(Identifier));
        }

        var clean = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
            {
                clean.Append(c);
            }
        }

        return clean.Length == 0 || char.IsAsciiDigit(clean[0]) ? "T" + clean : clean.ToString();
    }

    private static string Array(string element) => element.Contains(' ', StringComparison.Ordinal) ? $"({element})[]" : element + "[]";

    private static string Union(string type, string other) => type == "unknown" || type.Split(" | ").Contains(other) ? type : $"{type} | {other}";

    private static string Key(string name) => IsIdentifier(name) ? name : Quote(name);

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$');

    private static string Quote(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}

/// <summary>
/// The server under <c>--inertia-types</c>: the app starts (so its endpoints are known) but listens on nothing, so the declarations
/// can be written during a build or publish (<c>InertiaGenerateTypesOnBuild</c>) without binding a port.
/// </summary>
internal sealed class GenerateOnlyServer : Microsoft.AspNetCore.Hosting.Server.IServer
{
    public Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = CreateFeatures();

    public Task StartAsync<TContext>(Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
    }

    private static Microsoft.AspNetCore.Http.Features.FeatureCollection CreateFeatures()
    {
        var features = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        features.Set<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>(new NoAddresses());
        return features;
    }

    private sealed class NoAddresses : Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = [];

        public bool PreferHostingUrls { get; set; }
    }
}
