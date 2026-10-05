using FastEndpoints;
using FastEndpoints.Svelte;
using FastEndpointsSvelte;
using Inertia.Net.FastEndpoints;

var e2e = Environment.GetEnvironmentVariable("E2E") == "1";

var builder = WebApplication.CreateBuilder(args);

// The app's source-generated JSON context: Inertia.Net serializes prop values through these options (Native AOT friendly).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddFastEndpoints(DiscoveredTypes.All);
builder.Services.AddInertia(o =>
{
    o.Share("appName", "Inertia.Net Sample");
    o.VersionResolver = _ => AppState.Version; // null: fall back to the hash of the Vite manifest
});

var app = builder.Build();

app.UseStaticFiles();
app.UseInertia();
// Validation failures of Inertia requests become a redirect back with the errors (FluentValidation validators work as usual).
app.UseFastEndpoints(c =>
{
    c.UseInertia(ep => ep.AllowAnonymous());
    c.Serializer.Options.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    c.Binding.ReflectionCache.AddFromFastEndpointsSvelte(); // source-generated request DTO binding data (no expression compilation)
});

if (e2e)
{
    app.MapPost("/__e2e/bump-version", () => AppState.Version = Guid.NewGuid().ToString("N"));
    app.MapPost("/__e2e/reset", AppState.Reset);
}

app.Run();
