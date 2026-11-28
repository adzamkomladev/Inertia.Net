using System.ComponentModel.DataAnnotations;
#if (auth)
using System.Security.Claims;
#endif
using System.Text.Json;
using System.Text.Json.Serialization;
#if (auth)
using System.Threading.RateLimiting;
using InertiaStarter;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
#else
using InertiaStarter;
#endif

var builder = WebApplication.CreateBuilder(args);

// Props are serialized through the app's source-generated JSON context.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddValidation();
// CSRF: the Inertia client sends the XSRF-TOKEN cookie back as X-XSRF-TOKEN; UseInertiaAntiforgery() issues and validates it.
builder.Services.AddAntiforgery(o => o.HeaderName = InertiaHeaders.XsrfToken);
builder.Services.AddResponseCompression();
builder.Services.AddSingleton<TodoStore>();
#if (auth)
// The demo account signs in only in Development: a published app starts with it switched off.
builder.Services.AddSingleton(new UserStore(demoEnabled: builder.Environment.IsDevelopment()));
// Cookie sign-in. UseInertiaRedirects(): an Inertia visit to a protected page is redirected to /login like a browser request
// (303 after a POST, PUT or DELETE), not answered with 401.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.UseInertiaRedirects();
    });
builder.Services.AddAuthorization();
// At most 5 sign-in attempts a minute per client. Precognition requests (checking a field as you type) get their own allowance of 30 a
// minute, so typing doesn't use up sign-in attempts and validation can't be called without limit.
// Behind a reverse proxy every client has the proxy's address: configure forwarded headers first (README, "Deploying behind a proxy").
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", http =>
    {
        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // The metadata check keeps the Precognition header from skipping the limit on an endpoint whose handler runs for it.
        return http.GetEndpoint()?.Metadata.GetMetadata<InertiaPrecognitionMetadata>() is not null && InertiaPrecognition.IsPrecognitive(http.Request)
            ? RateLimitPartition.GetSlidingWindowLimiter("validation:" + client,
                _ => new SlidingWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 })
            : RateLimitPartition.GetFixedWindowLimiter("login:" + client,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) });
    });
    o.OnRejected = InertiaRateLimiting.OnRejected(); // back to the form, with flash.error
});
#endif
builder.Services.AddInertia(o =>
{
    o.Share("appName", "InertiaStarter");
#if (auth)
    // The signed-in user on every page (page.props.auth.user), typed in the generated SharedProps.
    o.Share("auth", http => new Auth(http.User.Identity?.IsAuthenticated == true
        ? new AuthUser(http.User.FindFirstValue(ClaimTypes.Name)!, http.User.FindFirstValue(ClaimTypes.Email)!)
        : null));
#endif
    o.Vite.Prefetch = VitePrefetch.Waterfall; // fetch the other pages' chunks once a page has loaded
    o.Vite.StartDevServer = true; // Development only: dotnet watch also runs the package's dev script (Vite) and stops it with the app
    // Development only: rendering a component without a page file fails fast, and ClientApp/types/inertia.d.ts (from these
    // C# types) and ClientApp/types/routes.ts (from the named endpoints below) are rewritten when the app starts.
    o.Pages.Directory = "ClientApp/pages";
    o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
    o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts";
    o.TypeScript.AddGeneratedPages().FlashData<Flash>(); // every IInertiaPage record, listed at compile time
});
// Render the Error page for production navigation errors; Development keeps its diagnostics.
builder.Services.AddInertiaErrorPages();
builder.Services.AddInertiaDevTools(); // the Inertia DevTools browser extension, in Development only

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Behind a proxy that terminates TLS, forward the scheme first (Inertia.Net README, "Deploying behind a proxy").
    app.UseHsts();
    app.UseHttpsRedirection();
}

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
app.UseInertiaErrorPages(); // before UseInertia(): also handles unmatched routes and unhandled exceptions
app.UseInertia();
#if (auth)
app.UseAuthentication(); // after UseInertiaErrorPages(): a 403 renders the Error page
app.UseAuthorization();
app.UseRateLimiter(); // after UseInertia(): a rejected sign-in redirects back with a flash message
app.UseInertiaAntiforgery(); // after UseAuthentication(): the token is tied to the signed-in user
#else
app.UseInertiaAntiforgery(); // after UseAuthentication() once the app has sign-in
#endif
app.MapInertiaDevTools();

app.MapInertia("/", HomePage.Component, new HomePage("Welcome to InertiaStarter, an Inertia.js app on ASP.NET Core.")).WithName("home");

// A named endpoint gets a typed function in ClientApp/types/routes.ts: routes.todos.index() is { url: '/todos', method: 'get' }.
app.MapGet("/todos", (TodoStore todos) => Render(new TodosPage(
    todos.All(),
    Defer(async ct =>
    {
        await Task.Delay(300, ct); // something slow: the page renders first, and the client loads this right after
        return todos.Stats();
    })))).WithName("todos.index");

// Invalid input never reaches the handler: the client gets a redirect back with the errors.
app.MapPost("/todos", IResult (HttpContext http, TodoStore todos, NewTodo input) =>
{
    if (!todos.TryAdd(input.Title!))
    {
        return Back("/todos").WithErrors(new Dictionary<string, string> { ["title"] = "The demo list is full. Delete a todo before adding another." });
    }

    http.Inertia().Flash("success", "Todo added");
    return Results.Redirect("/todos");
}).WithInertiaValidation().WithName("todos.store");

app.MapPatch("/todos/{id:int}/toggle", (TodoStore todos, int id) =>
{
    todos.Toggle(id);
    return Back("/todos");
}).WithName("todos.toggle");

app.MapDelete("/todos/{id:int}", (TodoStore todos, int id) =>
{
    todos.Remove(id);
    return Back("/todos").WithFlash("success", "Todo deleted");
}).WithName("todos.destroy");

#if (auth)
// The cookie handler sends signed-out visitors here with ?ReturnUrl=/the/page; the form posts it back.
app.MapGet("/login", (string? returnUrl, UserStore users) => Render(new LoginPage(returnUrl, users.DemoEnabled))).WithName("login");

// WithInertiaPrecognition(): the form checks each field as you leave it, with the same validation as the submit.
app.MapPost("/login", async (HttpContext http, UserStore users, LoginInput input) =>
{
    if (users.Find(input.Email!, input.Password!) is not { } user)
    {
        return Back("/login").WithErrors(new Dictionary<string, string> { ["email"] = "These credentials do not match our records." });
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Email, user.Email)], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identity));

    // Only a path on this site: an absolute or protocol-relative URL here would be an open redirect.
    return Results.LocalRedirect(RedirectHttpResult.IsLocalUrl(input.ReturnUrl) ? input.ReturnUrl! : "/dashboard");
}).WithInertiaPrecognition().RequireRateLimiting("login").WithName("login.store");

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();
    return Results.Redirect("/");
}).WithName("logout");

app.MapGet("/dashboard", (HttpContext http) => Render(new DashboardPage(http.User.FindFirstValue(ClaimTypes.Name)!)))
    .RequireAuthorization().WithName("dashboard");

#endif
app.Run();

namespace InertiaStarter
{
    // A page record names its client component once: Render(new HomePage(...)) and o.TypeScript.Page<HomePage>() both use it.
    public sealed record HomePage(string Message) : IInertiaPage
    {
        public static string Component => "Home";
    }

    public sealed record Todo(int Id, string Title, bool Done);

    public sealed record TodoStats(int Total, int Done);

    /// <summary>The Todos/Index props. Stats is deferred; the attribute gives it a type in the generated TypeScript.</summary>
    public sealed record TodosPage(List<Todo> Todos, [property: InertiaPropType(typeof(TodoStats))] InertiaProp Stats) : IInertiaPage
    {
        public static string Component => "Todos/Index";
    }

    /// <summary>The flash data the pages read (<c>page.flash</c>).</summary>
#if (auth)
    public sealed record Flash(string? Success, string? Error);
#else
    public sealed record Flash(string? Success);
#endif

    // Must be public: the AddValidation() source generator skips internal types.
    public sealed class NewTodo
    {
        [Required, StringLength(100)]
        public string? Title { get; set; }
    }

    /// <summary>In-memory demo list shared by every request, with room for 100 todos. Swap it for a database.</summary>
    public sealed class TodoStore
    {
        private const int Capacity = 100;
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

        public bool TryAdd(string title)
        {
            lock (_lock)
            {
                if (_todos.Count >= Capacity) return false;
                _todos.Add(new Todo(_nextId++, title, false));
                return true;
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

#if (auth)
    /// <summary>The login page. <c>DemoAccount</c> is true when the demo user can sign in (Development only).</summary>
    public sealed record LoginPage(string? ReturnUrl, bool DemoAccount) : IInertiaPage
    {
        public static string Component => "Auth/Login";
    }

    public sealed record DashboardPage(string Name) : IInertiaPage
    {
        public static string Component => "Dashboard";
    }

    /// <summary>The shared <c>auth</c> prop: the signed-in user, or null.</summary>
    public sealed record Auth(AuthUser? User);

    public sealed record AuthUser(string Name, string Email);

    public sealed class LoginInput
    {
        [Required, EmailAddress]
        public string? Email { get; set; }

        [Required]
        public string? Password { get; set; }

        /// <summary>Where to go after signing in: the page that sent the visitor to the login page.</summary>
        public string? ReturnUrl { get; set; }
    }

    /// <summary>
    /// One demo user (demo@example.com / password), accepted only when <c>demoEnabled</c> is true (Development). Swap it for
    /// ASP.NET Core Identity or your user database.
    /// </summary>
    public sealed class UserStore(bool demoEnabled)
    {
        private static readonly PasswordHasher<AuthUser> Hasher = new();
        private static readonly AuthUser Demo = new("Demo User", "demo@example.com");
        private readonly string _hash = Hasher.HashPassword(Demo, "password");

        public bool DemoEnabled => demoEnabled;

        public AuthUser? Find(string email, string password) =>
            demoEnabled
            && string.Equals(email, Demo.Email, StringComparison.OrdinalIgnoreCase)
            && Hasher.VerifyHashedPassword(Demo, _hash, password) != PasswordVerificationResult.Failed
                ? Demo
                : null;
    }

#endif
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(HomePage))]
    [JsonSerializable(typeof(TodosPage))]
    [JsonSerializable(typeof(TodoStats))]
    [JsonSerializable(typeof(Flash))]
    [JsonSerializable(typeof(NewTodo))]
#if (auth)
    [JsonSerializable(typeof(LoginPage))]
    [JsonSerializable(typeof(DashboardPage))]
    [JsonSerializable(typeof(Auth))]
    [JsonSerializable(typeof(LoginInput))]
#endif
    internal sealed partial class AppJsonContext : JsonSerializerContext;
}
