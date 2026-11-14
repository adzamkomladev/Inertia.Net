using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

public sealed record TypedTodosPage(List<string> Todos) : IInertiaPage
{
    public static string Component => "Todos/Index";
}

public sealed record ExplicitPage(int Count) : IInertiaPage
{
    static string IInertiaPage.Component => "Explicit/Show";
}

public sealed record OtherTodosPage(int Count) : IInertiaPage
{
    public static string Component => "Todos/Index";
}

[JsonSerializable(typeof(TypedTodosPage))]
[JsonSerializable(typeof(ExplicitPage))]
internal sealed partial class TypedPagesJsonContext : JsonSerializerContext;

/// <summary>Page records that name their component (<see cref="IInertiaPage"/>) and the generated <c>routes.ts</c>.</summary>
public sealed class TypedContractsTests
{
    [Fact]
    public async Task Render_uses_the_component_the_page_names()
    {
        var harness = new Harness();
        var context = harness.Context().AsInertia();
        await Harness.ExecuteAsync(context, Render(new TypedTodosPage(["a"])));
        var page = Harness.ParsePage(Harness.Body(context));
        Assert.Equal("Todos/Index", (string?)page["component"]);
        Assert.Equal("a", (string?)page["props"]!["todos"]![0]);

        // Explicit interface implementations work too, and the string overloads still bind.
        Assert.Equal("Explicit/Show", Render(new ExplicitPage(1)).Component);
        Assert.Equal("Plain", Render("Plain").Component);
        Assert.Equal("Plain", Render("Plain", new TypedTodosPage([])).Component);
    }

    [Fact]
    public void Page_without_a_name_generates_the_same_declarations_as_the_string_registration()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = TypedPagesJsonContext.Default };
        var byString = new InertiaOptions();
        byString.TypeScript.Page<TypedTodosPage>("Todos/Index");
        var byType = new InertiaOptions();
        byType.TypeScript.Page<TypedTodosPage>();
        Assert.Equal(new TypeScriptGenerator(json, byString).Generate(), new TypeScriptGenerator(json, byType).Generate());
    }

    [Fact]
    public void A_page_registered_by_type_and_by_string_is_declared_once()
    {
        var options = new InertiaOptions();
        options.TypeScript.Page<TypedTodosPage>().Page<TypedTodosPage>("Todos/Index").PagesFrom(typeof(TypedTodosPage).Assembly);
        Assert.Single(options.TypeScript.Pages, p => p.Component == "Todos/Index" && p.Props == typeof(TypedTodosPage));
        Assert.Contains(("Explicit/Show", typeof(ExplicitPage)), options.TypeScript.Pages);
    }

    [Fact]
    public void A_name_registered_with_two_types_fails_validation()
    {
        var options = new InertiaOptions();
        options.TypeScript.Page<TypedTodosPage>().Page<OtherTodosPage>();
        var result = new InertiaOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains("'Todos/Index'", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_directory_check_keys_on_the_component_the_page_names()
    {
        using var root = new TempRoot();
        root.Write("app.html", "<html><head>@inertiaHead</head><body>@inertia</body></html>");
        root.Write("ClientApp/pages/Todos/Index.tsx", "export default () => null");
        var harness = new Harness(o => o.Pages.Directory = "ClientApp/pages", contentRoot: root.Path, environment: "Development");
        await Harness.ExecuteAsync(harness.Context().AsInertia(), Render(new TypedTodosPage([])));
        await Assert.ThrowsAnyAsync<Exception>(() => Harness.ExecuteAsync(harness.Context().AsInertia(), Render(new ExplicitPage(1))));
    }

    private static RouteEndpoint Endpoint(string pattern, string? name, params string[] methods)
    {
        var metadata = new List<object>();
        if (name is not null)
        {
            metadata.Add(new EndpointNameMetadata(name));
        }

        if (methods.Length > 0)
        {
            metadata.Add(new HttpMethodMetadata(methods));
        }

        return new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(metadata), pattern);
    }

    [Fact]
    public void Routes_cover_every_parameter_kind()
    {
        var generator = new RoutesGenerator();
        var ts = generator.Generate(
        [
            Endpoint("/todos/{id:int}", "todos.show", "GET", "HEAD"),
            Endpoint("/todos", "todos.index", "GET"),
            Endpoint("/todos", "todos.store", "POST"),
            Endpoint("/todos/{id:guid}", "todos.update", "PUT", "PATCH"),
            Endpoint("/posts/{slug}/{page=1}", "posts.show", "GET"),
            Endpoint("/files/{*path}", "files", "GET"),
            Endpoint("/search/{term?}", "search", "GET"),
            Endpoint("/flags/{on:bool}", "flags", "GET"),
            Endpoint("/kebab", "with-dash", "GET"),
            Endpoint("/hidden", null, "GET"),
        ]).ReplaceLineEndings("\n");

        Assert.Equal(1, generator.Unnamed);
        Assert.Empty(generator.Warnings);
        Assert.Contains("const r5_url = (args: { id: number } | [id: number] | number, options?: RouteOptions): string => build([['todos'], [['id', false, null, false]]], ['id'], args, options)", ts, StringComparison.Ordinal);
        Assert.Contains("definition: { methods: ['get'] as const, url: '/todos/{id}' }", ts, StringComparison.Ordinal);
        Assert.Contains("definition: { methods: ['put', 'patch'] as const, url: '/todos/{id}' }", ts, StringComparison.Ordinal);
        Assert.Contains("args: { id: string } | [id: string] | string", ts, StringComparison.Ordinal);
        Assert.Contains("args: { slug: string | number; page?: string | number } | [slug: string | number, page?: string | number]", ts, StringComparison.Ordinal);
        Assert.Contains("[['posts'], [['slug', false, null, false]], [['page', true, '1', false]]]", ts, StringComparison.Ordinal);
        Assert.Contains("[['files'], [['path', true, null, true]]]", ts, StringComparison.Ordinal);
        Assert.Contains("args?: { term?: string | number }", ts, StringComparison.Ordinal);
        Assert.Contains("args: { on: boolean }", ts, StringComparison.Ordinal);
        Assert.Contains("""
            export const routes = {
              files: r0,
              flags: r1,
              posts: {
                show: r2,
              },
              search: r3,
              todos: {
                index: r4,
                show: r5,
                store: r6,
                update: r7,
              },
              'with-dash': r8,
            }
            """.ReplaceLineEndings("\n"), ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Routes_output_is_deterministic_and_reports_collisions()
    {
        RouteEndpoint[] endpoints =
        [
            Endpoint("/b", "same", "GET"),
            Endpoint("/a", "same", "GET"),
            Endpoint("/x", "dup", "GET"),
            Endpoint("/x", "dup", "POST"),
            Endpoint("/o", "opt", "OPTIONS"),
            Endpoint("/n", "parent", "GET"),
            Endpoint("/n/url", "parent.url", "GET"),
        ];
        var generator = new RoutesGenerator();
        var first = generator.Generate(endpoints);
        Assert.Equal(first, new RoutesGenerator().Generate(endpoints.Reverse()));
        Assert.Contains(generator.Warnings, w => w.Contains("'same' names both '/a' and '/b'", StringComparison.Ordinal));
        Assert.Contains(generator.Warnings, w => w.Contains("options", StringComparison.Ordinal));
        Assert.Contains(generator.Warnings, w => w.Contains("'parent.url'", StringComparison.Ordinal));
        Assert.Contains("url: '/a'", first, StringComparison.Ordinal);
        Assert.DoesNotContain("url: '/b'", first, StringComparison.Ordinal);
        Assert.Contains("methods: ['get', 'post'] as const, url: '/x'", first, StringComparison.Ordinal);
        Assert.DoesNotContain("'/o'", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Registered_live_events_are_exported_as_constants()
    {
        var orders = new LiveEvent("OrderUpdated", LiveChannel.Private("orders.1"));
        var options = new InertiaOptions();
        options.TypeScript.LiveEvents(orders, orders, new LiveEvent("it's", LiveChannel.Public("feed")), new LiveEvent("OrderUpdated", LiveChannel.Public("other")));
        var generator = new RoutesGenerator(liveEvents: options.TypeScript.LiveEventList);
        var ts = generator.Generate([]);
        Assert.Contains("""
            export const liveEvents = {
              OrderUpdated: { name: 'OrderUpdated', channel: { type: 'private', name: 'orders.1' } },
              'it\'s': { name: 'it\'s', channel: { type: 'public', name: 'feed' } },
            } as const
            """.ReplaceLineEndings("\n"), ts, StringComparison.Ordinal);
        Assert.Single(generator.Warnings);
        Assert.DoesNotContain("liveEvents", new RoutesGenerator().Generate([]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_is_also_a_prefix_becomes_a_function_with_members()
    {
        var ts = new RoutesGenerator().Generate([Endpoint("/todos", "todos", "GET"), Endpoint("/todos/new", "todos.create", "GET")]);
        Assert.Contains("todos: Object.assign(r0, {", ts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Routes_are_written_from_the_app_endpoints_only_when_the_path_is_set_and_content_changed()
    {
        using var root = new TempRoot();
        var path = Path.Join(root.Path, "ClientApp/types/routes.ts");
        var harness = new Harness(
            o => o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts",
            configureServices: s => s.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(Endpoint("/todos/{id:int}", "todos.show", "GET"), Endpoint("/unnamed", null, "GET"))),
            contentRoot: root.Path,
            environment: "Development");
        var writer = harness.Services.GetServices<IHostedService>().OfType<InertiaTypeScriptWriter>().Single();

        await writer.StartAsync(TestContext.Current.CancellationToken);
        Assert.Contains("show: r0", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var written = File.GetLastWriteTimeUtc(path).AddHours(-1);
        File.SetLastWriteTimeUtc(path, written);
        await writer.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));

        var unset = new Harness(contentRoot: root.Path, environment: "Development");
        await unset.Services.GetServices<IHostedService>().OfType<InertiaTypeScriptWriter>().Single().StartAsync(TestContext.Current.CancellationToken);
        Assert.Single(Directory.GetFiles(Path.Join(root.Path, "ClientApp/types")));
    }

    [Fact]
    public async Task The_generated_url_builder_produces_the_urls_aspnetcore_matches()
    {
        Assert.SkipUnless(SsrNodeProcessTests.NodeAvailable, "node is not on PATH");
        using var root = new TempRoot();
        root.Write("routes.ts", new RoutesGenerator().Generate(
        [
            Endpoint("/todos/{id:int}", "todos.show", "GET"),
            Endpoint("/todos/{id:int}", "todos.update", "PUT", "PATCH"),
            Endpoint("/posts/{slug}/{page=1}", "posts.show", "GET"),
            Endpoint("/archive/{year=2024}/{month=1}", "archive", "GET"),
            Endpoint("/files/{*path}", "files", "GET"),
            Endpoint("/search/{term?}", "search", "GET"),
            Endpoint("/", "home", "GET"),
        ]));
        // Node 23.6+ runs TypeScript by stripping the types, so this also checks that the file is plain erasable TypeScript.
        root.Write("check.ts", """
            import assert from 'node:assert/strict'
            import { routes, routeConfig } from './routes.ts'
            assert.deepEqual(routes.todos.show(1), { url: '/todos/1', method: 'get' })
            assert.deepEqual(routes.todos.show({ id: 2 }), { url: '/todos/2', method: 'get' })
            assert.deepEqual(routes.todos.show([3]), { url: '/todos/3', method: 'get' })
            assert.deepEqual(routes.todos.update(4), { url: '/todos/4', method: 'put' })
            assert.deepEqual(routes.todos.update.patch(4), { url: '/todos/4', method: 'patch' })
            assert.deepEqual(routes.todos.update.definition, { methods: ['put', 'patch'], url: '/todos/{id}' })
            assert.equal(routes.todos.show.url(1, { query: { tab: 'x y', tags: ['a', 'b'], none: null } }), '/todos/1?tab=x+y&tags=a&tags=b')
            assert.equal(routes.posts.show.url({ slug: 'a/b é' }), '/posts/a%2Fb%20%C3%A9')
            assert.equal(routes.posts.show.url({ slug: 'a', page: 2 }), '/posts/a/2')
            assert.equal(routes.archive.url(), '/archive')
            assert.equal(routes.archive.url({ month: 3 }), '/archive/2024/3')
            assert.equal(routes.files.url('docs/a b.txt'), '/files/docs/a%20b.txt')
            assert.equal(routes.search.url(), '/search')
            assert.equal(routes.search.url('q'), '/search/q')
            assert.equal(routes.home.url(), '/')
            assert.throws(() => routes.todos.show.url({} as never), /'id' is required/)
            routeConfig.base = '/app'
            assert.equal(routes.todos.show.url(1), '/app/todos/1')
            assert.equal(routes.home.url(), '/app/')
            console.log('ok')
            """);
        using var node = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "check.ts")
        {
            WorkingDirectory = root.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = await node.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = await node.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await node.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(node.ExitCode == 0, error);
        Assert.Equal("ok", output.Trim());
    }
}
