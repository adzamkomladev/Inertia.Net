using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertiaStarter;

var builder = WebApplication.CreateBuilder(args);

// Props are serialized through the app's source-generated JSON context.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddValidation();
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddResponseCompression();
builder.Services.AddSingleton<TodoStore>();
builder.Services.AddInertia(o =>
{
    o.Share("appName", "InertiaStarter");
    o.Vite.Prefetch = VitePrefetch.Waterfall; // fetch the other pages' chunks once a page has loaded
    // Development only: rendering a component without a page file fails fast, and ClientApp/types/inertia.d.ts is rewritten
    // from these C# types when the app starts.
    o.Pages.Directory = "ClientApp/pages";
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
    o.TypeScript.Page<HomePage>("Home").Page<TodosPage>("Todos/Index").FlashData<Flash>();
});
builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only

var app = builder.Build();

app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // Vite puts a content hash in every file name under build/assets, so browsers can keep them for good.
    OnPrepareResponse = c =>
    {
        if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
        {
            c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
app.UseInertia();
app.UseInertiaAntiforgery(); // after UseAuthentication() once the app has sign-in
app.MapInertiaDevTools();

app.MapInertia("/", "Home", new HomePage("Welcome to InertiaStarter, an Inertia.js app on ASP.NET Core."));

app.MapGet("/todos", (TodoStore todos) => Render("Todos/Index", new TodosPage(
    todos.All(),
    Defer(async ct =>
    {
        await Task.Delay(300, ct); // something slow: the page renders first, and the client loads this right after
        return todos.Stats();
    }))));

// Invalid input never reaches the handler: the client gets a redirect back with the errors.
app.MapPost("/todos", (HttpContext http, TodoStore todos, NewTodo input) =>
{
    todos.Add(input.Title!);
    http.Inertia().Flash("success", "Todo added");
    return Results.Redirect("/todos");
}).WithInertiaValidation();

app.MapPatch("/todos/{id:int}/toggle", (TodoStore todos, int id) =>
{
    todos.Toggle(id);
    return Back("/todos");
});

app.MapDelete("/todos/{id:int}", (TodoStore todos, int id) =>
{
    todos.Remove(id);
    return Back("/todos").WithFlash("success", "Todo deleted");
});

app.Run();

namespace InertiaStarter
{
    public sealed record HomePage(string Message);

    public sealed record Todo(int Id, string Title, bool Done);

    public sealed record TodoStats(int Total, int Done);

    /// <summary>The Todos/Index props. Stats is deferred; the attribute gives it a type in the generated TypeScript.</summary>
    public sealed record TodosPage(List<Todo> Todos, [property: InertiaPropType(typeof(TodoStats))] InertiaProp Stats);

    /// <summary>The flash data the pages read (<c>page.flash</c>).</summary>
    public sealed record Flash(string? Success);

    // Must be public: the AddValidation() source generator skips internal types.
    public sealed class NewTodo
    {
        [Required, StringLength(100)]
        public string? Title { get; set; }
    }

    /// <summary>In-memory todos shared by every request. Swap it for a database.</summary>
    public sealed class TodoStore
    {
        private readonly Lock _lock = new();
        private readonly List<Todo> _todos = [new(1, "Read the Inertia.Net README", false), new(2, "Build something", false)];
        private int _nextId = 3;

        public List<Todo> All()
        {
            lock (_lock)
            {
                return [.. _todos];
            }
        }

        public TodoStats Stats()
        {
            lock (_lock)
            {
                return new TodoStats(_todos.Count, _todos.Count(t => t.Done));
            }
        }

        public void Add(string title)
        {
            lock (_lock)
            {
                _todos.Add(new Todo(_nextId++, title, false));
            }
        }

        public void Toggle(int id)
        {
            lock (_lock)
            {
                var index = _todos.FindIndex(t => t.Id == id);
                if (index >= 0)
                {
                    _todos[index] = _todos[index] with { Done = !_todos[index].Done };
                }
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                _todos.RemoveAll(t => t.Id == id);
            }
        }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(HomePage))]
    [JsonSerializable(typeof(TodosPage))]
    [JsonSerializable(typeof(TodoStats))]
    [JsonSerializable(typeof(Flash))]
    [JsonSerializable(typeof(NewTodo))]
    internal sealed partial class AppJsonContext : JsonSerializerContext;
}
