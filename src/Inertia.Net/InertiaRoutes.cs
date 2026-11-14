using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Inertia.Net;

/// <summary>What the core cannot read from endpoint metadata alone, per host package, for <c>routes.ts</c>.</summary>
internal interface IRouteBodySource
{
    /// <summary>The request-body type (MVC: the <c>[FromBody]</c> parameter), or null.</summary>
    Type? BodyType(Endpoint endpoint);

    /// <summary>True when the host named the endpoint itself (FastEndpoints names every endpoint after its class).</summary>
    bool IsGeneratedName(Endpoint endpoint, string name) => false;
}

/// <summary>
/// Writes <c>routes.ts</c> (<see cref="InertiaTypeScriptOptions.RoutesOutputPath"/>): one function per named endpoint, in the shape the
/// Inertia client accepts wherever it takes a URL (<c>{ url, method }</c>), with <c>.url()</c>, <c>.definition</c> and a variant per HTTP method.
/// </summary>
internal sealed class RoutesGenerator(TypeScriptGenerator? types = null, IEnumerable<IRouteBodySource>? bodySources = null, IEnumerable<LiveEvent>? liveEvents = null)
{
    /// <summary>Sources a host package registers when it loads (FastEndpoints has no service registration of its own).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IRouteBodySource> Registered = new();

    private readonly IRouteBodySource[] _sources = [.. bodySources ?? [], .. Registered.Values];

    /// <summary>Top-level names the file declares, kept free for the request-body types.</summary>
    internal static readonly string[] ReservedNames = ["routeConfig", "Method", "RouteDefinition", "QueryScalar", "RouteOptions", "Part", "build", "routes", "RouteBodies", "liveEvents"];

    private static readonly string[] ClientMethods = ["get", "post", "put", "patch", "delete", "query"];

    // Members every generated route function already has; a child route with one of these names would overwrite it.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "url", "definition", "name", "length", "prototype", "call", "apply", "bind", "caller", "arguments", "toString",
        "get", "post", "put", "patch", "delete", "query",
    };

    public List<string> Warnings { get; } = [];

    public int Unnamed { get; private set; }

    public string Generate(IEnumerable<Endpoint> endpoints)
    {
        var routes = new SortedDictionary<string, Route>(StringComparer.Ordinal);
        // Ordered so that collisions and merged methods come out the same whatever order the data sources list endpoints in.
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>()
            .OrderBy(e => e.RoutePattern.RawText, StringComparer.Ordinal)
            .ThenBy(e => string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []), StringComparer.Ordinal))
        {
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName;
            if (string.IsNullOrEmpty(name) || _sources.Any(s => s.IsGeneratedName(endpoint, name)))
            {
                Unnamed++;
                continue;
            }

            var methods = (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => m.ToLowerInvariant())
                .Where(m => m != "head")
                .ToList();
            var unsupported = methods.Where(m => !ClientMethods.Contains(m)).ToList();
            if (unsupported.Count > 0)
            {
                Warnings.Add($"route '{name}': {string.Join(", ", unsupported)} cannot be sent by the Inertia client and is left out.");
                methods.RemoveAll(unsupported.Contains);
            }

            if (methods.Count == 0)
            {
                continue;
            }

            var pattern = Template(endpoint.RoutePattern);
            if (routes.TryGetValue(name, out var existing))
            {
                if (existing.Pattern.Template != pattern.Template)
                {
                    Warnings.Add($"route '{name}' names both '{existing.Pattern.Template}' and '{pattern.Template}'; only the first is generated.");
                    continue;
                }

                existing.Methods.AddRange(methods.Where(m => !existing.Methods.Contains(m)));
                continue;
            }

            // Only these methods carry a body the client sends as form data; GET/DELETE request DTOs are bound from the route and query.
            routes[name] = new Route(name, pattern, methods) { Body = methods.Any(m => m is "post" or "put" or "patch") ? BodyType(endpoint) : null };
        }

        var text = new StringBuilder();
        text.Append(Preamble);
        var index = 0;
        foreach (var route in routes.Values)
        {
            route.Identifier = "r" + index++.ToString(CultureInfo.InvariantCulture);
            WriteRoute(text, route);
        }

        // Dots nest: todos.show becomes routes.todos.show; a name that is also a prefix (todos, todos.show) is a function with members.
        var root = new Node();
        foreach (var route in routes.Values)
        {
            var node = root;
            var segments = route.Name.Split('.');
            var skipped = false;
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || (node.Route is not null && Reserved.Contains(segment)))
                {
                    skipped = true;
                    break;
                }

                if (!node.Children.TryGetValue(segment, out var child))
                {
                    node.Children[segment] = child = new Node();
                }

                node = child;
            }

            if (skipped || (node.Route is null && node.Children.Keys.Any(Reserved.Contains)))
            {
                Warnings.Add($"route '{route.Name}' is not a valid route path (empty segment, or a member such as 'url' of another route); it is left out.");
                continue;
            }

            node.Route = route;
        }

        text.Append("export const routes = ").Append(Literal(root, "")).Append('\n');

        // Live events, for liveReload(live, liveEvents.OrderUpdated, ...): the names the server publishes, not retyped on the client.
        if (liveEvents?.Any() == true)
        {
            text.Append("\nexport const liveEvents = {\n");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in liveEvents)
            {
                if (!seen.Add(e.Name))
                {
                    Warnings.Add($"live event '{e.Name}' on channel '{e.Channel.Name}' has the name of another registered event; it is left out.");
                    continue;
                }

                text.Append("  ").Append(Key(e.Name)).Append(": { name: ").Append(Quote(e.Name)).Append(", channel: { type: ")
                    .Append(Quote(e.Channel.TypeName)).Append(", name: ").Append(Quote(e.Channel.Name)).Append(" } },\n");
            }

            text.Append("} as const\n");
        }

        // Request bodies, for useForm<RouteBodies['todos.store']>(): typed like page props, from the app's JSON contracts.
        if (types is not null && routes.Values.Any(r => r.Body is not null))
        {
            text.Append("\nexport interface RouteBodies {\n");
            foreach (var route in routes.Values.Where(r => r.Body is not null))
            {
                text.Append("  ").Append(Quote(route.Name)).Append(": ").Append(types.Reference(route.Body!)).Append('\n');
            }

            text.Append("}\n\n").Append(types.Declarations());
            Warnings.AddRange(types.Warnings);
        }

        return text.ToString();
    }

    private static string Literal(Node node, string indent)
    {
        var members = new StringBuilder("{\n");
        foreach (var (key, child) in node.Children)
        {
            members.Append(indent).Append("  ").Append(Key(key)).Append(": ");
            members.Append(child.Children.Count == 0 ? child.Route!.Identifier : child.Route is null
                ? Literal(child, indent + "  ")
                : $"Object.assign({child.Route.Identifier}, {Literal(child, indent + "  ")})");
            members.Append(",\n");
        }

        return members.Append(indent).Append('}').ToString();
    }

    private static void WriteRoute(StringBuilder text, Route route)
    {
        var parameters = route.Pattern.Parameters;
        var id = route.Identifier!;
        var methods = route.Methods;
        string args;
        string signature;
        if (parameters.Count == 0)
        {
            args = "undefined";
            signature = "options?: RouteOptions";
        }
        else
        {
            var fields = string.Join("; ", parameters.Select(p => $"{Key(p.Name)}{(p.Required ? "" : "?")}: {p.Type}"));
            var tuple = string.Join(", ", parameters.Select(p => $"{Identifier(p.Name)}{(p.Required ? "" : "?")}: {p.Type}"));
            var type = $"{{ {fields} }} | [{tuple}]";
            if (parameters.Count == 1)
            {
                type += $" | {parameters[0].Type}";
            }

            var optional = parameters.All(p => !p.Required);
            args = "args";
            signature = $"args{(optional ? "?" : "")}: {type}, options?: RouteOptions";
        }

        var names = "[" + string.Join(", ", parameters.Select(p => Quote(p.Name))) + "]";
        var call = parameters.Count == 0 ? "options" : "args, options";
        text.Append("const ").Append(id).Append("_url = (").Append(signature).Append("): string => build(")
            .Append(route.Pattern.Segments).Append(", ").Append(names).Append(", ").Append(args).Append(", options)\n");
        text.Append("const ").Append(id).Append(" = Object.assign(\n  (").Append(signature).Append("): RouteDefinition<")
            .Append(Quote(methods[0])).Append("> => ({ url: ").Append(id).Append("_url(").Append(call).Append("), method: ")
            .Append(Quote(methods[0])).Append(" }),\n  {\n    url: ").Append(id).Append("_url,\n    definition: { methods: [")
            .Append(string.Join(", ", methods.Select(Quote))).Append("] as const, url: ").Append(Quote(route.Pattern.Template)).Append(" },\n");
        foreach (var method in methods)
        {
            text.Append("    ").Append(method).Append(": (").Append(signature).Append("): RouteDefinition<").Append(Quote(method))
                .Append("> => ({ url: ").Append(id).Append("_url(").Append(call).Append("), method: ").Append(Quote(method)).Append(" }),\n");
        }

        text.Append("  },\n)\n\n");
    }

    // Minimal API and FastEndpoints describe the body with IAcceptsMetadata; MVC through a source its package registers.
    private Type? BodyType(Endpoint endpoint)
    {
        foreach (var source in _sources)
        {
            if (source.BodyType(endpoint) is { } type)
            {
                return type;
            }
        }

        return endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType is { } body && body != typeof(object) ? body : null;
    }

    // The pattern as the client builds it: segments of literal and parameter parts, the display template, and the typed parameters.
    private static Pattern Template(RoutePattern pattern)
    {
        var segments = new List<string>();
        var template = new StringBuilder();
        var parameters = new List<Parameter>();
        foreach (var segment in pattern.PathSegments)
        {
            var parts = new List<string>();
            template.Append('/');
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        parts.Add(Quote(literal.Content));
                        template.Append(literal.Content);
                        break;
                    case RoutePatternSeparatorPart separator:
                        parts.Add(Quote(separator.Content));
                        template.Append(separator.Content);
                        break;
                    case RoutePatternParameterPart parameter:
                        var fallback = parameter.Default is { } value ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
                        var required = !parameter.IsOptional && fallback is null && !parameter.IsCatchAll;
                        parameters.Add(new Parameter(parameter.Name, required, Type(parameter)));
                        parts.Add($"[{Quote(parameter.Name)}, {(required ? "false" : "true")}, {(fallback is null ? "null" : Quote(fallback))}, {(parameter.IsCatchAll ? "true" : "false")}]");
                        template.Append('{').Append(parameter.IsCatchAll ? "*" : "").Append(parameter.Name).Append(parameter.IsOptional ? "?" : "").Append('}');
                        break;
                }
            }

            segments.Add("[" + string.Join(", ", parts) + "]");
        }

        return new Pattern("[" + string.Join(", ", segments) + "]", template.Length == 0 ? "/" : template.ToString(), parameters);
    }

    private static string Type(RoutePatternParameterPart parameter)
    {
        var names = parameter.ParameterPolicies.Select(p => p.Content is { } content ? content.Split('(')[0].ToLowerInvariant() : null).ToList();
        if (names.Any(n => n is "int" or "long" or "decimal" or "double" or "float" or "min" or "max" or "range"))
        {
            return "number";
        }

        if (names.Contains("bool"))
        {
            return "boolean";
        }

        return names.Count > 0 ? "string" : "string | number";
    }

    private static string Key(string name) => IsIdentifier(name) ? name : Quote(name);

    // A tuple label must be an identifier; it is only a name in the IDE, so anything else becomes one.
    private static string Identifier(string name) => IsIdentifier(name) ? name : "_" + string.Concat(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$'));

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$');

    private static string Quote(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    private sealed record Parameter(string Name, bool Required, string Type);

    private sealed record Pattern(string Segments, string Template, List<Parameter> Parameters);

    private sealed class Route(string name, Pattern pattern, List<string> methods)
    {
        public string Name { get; } = name;

        public Pattern Pattern { get; } = pattern;

        public List<string> Methods { get; } = methods;

        public string? Identifier { get; set; }

        public Type? Body { get; init; }
    }

    private sealed class Node
    {
        public SortedDictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public Route? Route { get; set; }
    }

    private const string Preamble = """
        // Generated by Inertia.Net from the app's named endpoints. Do not edit: it is rewritten when the app starts in Development
        // or runs with --inertia-types.

        /** Prefixed to every URL: set it once when the app runs under a sub-path, e.g. routeConfig.base = '/app'. */
        export const routeConfig = { base: '' }

        export type Method = 'get' | 'post' | 'put' | 'patch' | 'delete' | 'query'
        export type RouteDefinition<M extends Method = Method> = { url: string; method: M }
        type QueryScalar = string | number | boolean
        export type RouteOptions = { query?: Record<string, QueryScalar | QueryScalar[] | null | undefined> }
        type Part = string | [name: string, optional: boolean, fallback: string | null, catchAll: boolean]

        function build(segments: Part[][], names: string[], args: unknown, options?: RouteOptions): string {
          const values: Record<string, unknown> =
            Array.isArray(args) ? Object.fromEntries(names.map((name, i) => [name, args[i]]))
            : args !== null && typeof args === 'object' ? (args as Record<string, unknown>)
            : names.length > 0 ? { [names[0]]: args } : {}
          const path: string[] = []
          let omitted: string[] = [] // trailing segments left to their default are dropped, as ASP.NET Core's link generator does
          for (const segment of segments) {
            let text = ''
            let missing = false
            for (const part of segment) {
              if (typeof part === 'string') {
                text += part
                continue
              }
              const [name, optional, fallback, catchAll] = part
              const value = values[name]
              if (value === undefined || value === null || value === '') {
                if (!optional) throw new Error(`Route parameter '${name}' is required.`)
                missing = true
                if (fallback !== null) text += encodeURIComponent(fallback)
                continue
              }
              const encoded = encodeURIComponent(String(value))
              text += catchAll ? encoded.replace(/%2F/gi, '/') : encoded
            }
            if (missing && segment.length === 1) {
              omitted.push(text)
            } else {
              path.push(...omitted, text)
              omitted = []
            }
          }
          let url = routeConfig.base + '/' + path.filter((s) => s !== '').join('/')
          const search = new URLSearchParams()
          for (const [key, value] of Object.entries(options?.query ?? {})) {
            for (const item of Array.isArray(value) ? value : [value]) {
              if (item !== null && item !== undefined) search.append(key, String(item))
            }
          }
          const query = search.toString()
          if (query !== '') url += '?' + query
          return url
        }


        """;
}
