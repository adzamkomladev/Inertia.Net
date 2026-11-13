using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Inertia.Net.Inertia;

namespace Inertia.Net.Tests;

/// <summary>The Development pages check (<see cref="InertiaOptions.Pages"/>) and the TypeScript declarations (<see cref="InertiaOptions.TypeScript"/>).</summary>
public sealed class DeveloperExperienceTests : IDisposable
{
    private readonly TempRoot _root = new();

    public DeveloperExperienceTests()
    {
        _root.Write("app.html", "<html><head>@inertiaHead</head><body>@inertia</body></html>");
        _root.Write("ClientApp/pages/Users/Index.tsx", "export default () => null");
        _root.Write("ClientApp/pages/Home.vue", "<template />");
        _root.Write("ClientApp/pages/readme.md", "not a page");
    }

    public void Dispose() => _root.Dispose();

    private Harness Pages(string environment = "Development") =>
        new(o => o.Pages.Directory = "ClientApp/pages", contentRoot: _root.Path, environment: environment);

    [Fact]
    public void Repeated_AddInertia_registers_each_hosted_service_once()
    {
        var harness = new Harness(configureServices: services =>
        {
            services.AddInertia();
            services.AddInertia();
        });

        var hostedServices = harness.Services.GetServices<IHostedService>().ToArray();
        Assert.Single(hostedServices.OfType<InertiaTypeScriptWriter>());
        Assert.Same(harness.Services.GetRequiredService<InertiaRefreshQueue>(),
            Assert.Single(hostedServices.OfType<InertiaRefreshQueue>()));
        Assert.Same(harness.Services.GetRequiredService<SsrNodeProcess>(),
            Assert.Single(hostedServices.OfType<SsrNodeProcess>()));
    }

    [Fact]
    public async Task Existing_components_render()
    {
        var harness = Pages();
        await Harness.ExecuteAsync(harness.Context(), Render("Users/Index"));
        await Harness.ExecuteAsync(harness.Context().AsInertia(), Render("Home"));
    }

    [Theory]
    [InlineData("Users/Indx", "Users/Index")]
    [InlineData("users/index", "Users/Index")]
    [InlineData("Hom", "Home")]
    [InlineData("Completely/Different/Name", null)]
    [InlineData("readme", null)]
    public async Task Missing_components_fail_with_the_closest_name(string component, string? suggestion)
    {
        var harness = Pages();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Harness.ExecuteAsync(harness.Context(), Render(component)));
        Assert.Contains($"'{component}' was not found in 'ClientApp/pages'", error.Message, StringComparison.Ordinal);
        if (suggestion is null)
        {
            Assert.DoesNotContain("Did you mean", error.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"Did you mean '{suggestion}'?", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_component_added_after_the_first_scan_is_found()
    {
        var harness = Pages();
        await Harness.ExecuteAsync(harness.Context(), Render("Home"));
        _root.Write("ClientApp/pages/New.svelte", "");
        await Harness.ExecuteAsync(harness.Context(), Render("New"));
    }

    [Fact]
    public async Task Production_never_checks()
    {
        var harness = Pages("Production");
        await Harness.ExecuteAsync(harness.Context().AsInertia(), Render("Nowhere"));
        Assert.False(harness.Services.GetRequiredService<PageComponentCheck>().Enabled);
    }

    private static string Generate(Action<InertiaOptions> configure, JsonSerializerOptions? json = null)
    {
        var options = new InertiaOptions();
        configure(options);
        json ??= new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = TypeScriptJsonContext.Default };
        return new TypeScriptGenerator(json, options).Generate().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Pages_get_interfaces_that_follow_the_json_contract()
    {
        var ts = Generate(o => o.TypeScript.Page<UsersPage>("Users/Index").Page<OrdersPage>("Orders/Show"));

        Assert.Contains(
            """
            export interface InertiaPages {
              'Users/Index': UsersPage
              'Orders/Show': OrdersPage
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface UsersPage {
              users: TsUser[]
              total: number
              filter: string | null
              stats?: unknown
              admins?: TsUser[]
              byRole: Record<string, TsUser[]>
              'display-name': string
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface TsUser {
              id: number
              name: string
              status: Status
              createdAt: string
              score: number | null
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains("export type Status = 'active' | 'disabled'", ts, StringComparison.Ordinal);
        Assert.Contains("/** Low = 0, Normal = 1, High = 2 */\nexport type Priority = 0 | 1 | 2", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("/** active", ts, StringComparison.Ordinal);
        Assert.Contains("export type Shape = Circle | Square", ts, StringComparison.Ordinal);
        Assert.Contains(
            """
            export interface Circle {
              kind: 'circle'
              radius: number
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains("lines: PagedTsUser", ts, StringComparison.Ordinal);
        Assert.Contains("export interface PagedTsUser {", ts, StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Union_props_are_typed_as_a_union_of_their_cases()
    {
        var ts = Generate(o => o.TypeScript.Page<UnionPage>("Union"));

        Assert.Contains("subject: TsUser | string", ts, StringComparison.Ordinal);
        Assert.Contains("export interface TsUser {", ts, StringComparison.Ordinal);
    }

#endif
    [Fact]
    public void Shared_props_are_inferred_from_the_shares_and_feed_the_inertia_config()
    {
        var ts = Generate(o =>
        {
            o.Share("appName", "Demo");
            o.Share("auth.user", new TsUser(1, "Ann", Status.Active, DateTime.UnixEpoch, null));
            o.Share("plans", Optional(() => new List<string>()));
            o.Share("now", _ => DateTime.UtcNow);
            o.Share("requestId", (Func<HttpContext, object?>)(c => c.TraceIdentifier));
            o.WithAllErrors = true;
            o.TypeScript.FlashData<Flash>();
        });

        Assert.Contains(
            """
            export interface SharedProps {
              appName: string
              auth: {
                user: TsUser
              }
              now: string
              plans?: string[]
              requestId: unknown
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
        Assert.Contains("export interface FlashData extends Flash {}", ts, StringComparison.Ordinal);
        Assert.Contains(
            """
            declare module '@inertiajs/core' {
              export interface InertiaConfig {
                sharedPageProps: SharedProps
                flashDataType: FlashData
                errorValueType: string[]
              }
            }
            """.ReplaceLineEndings("\n"),
            ts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_shared_props_type_wins_and_missing_metadata_is_unknown()
    {
        var generator = new TypeScriptGenerator(
            new JsonSerializerOptions { TypeInfoResolver = TypeScriptJsonContext.Default },
            Configure(o => o.TypeScript.SharedProps<Flash>().Page<Unregistered>("Missing")));
        var ts = generator.Generate();
        Assert.Contains("export interface SharedProps extends Flash {}", ts, StringComparison.Ordinal);
        Assert.Contains("Missing: unknown", ts, StringComparison.Ordinal);
        Assert.Contains(generator.Warnings, w => w.Contains("Unregistered", StringComparison.Ordinal));

        static InertiaOptions Configure(Action<InertiaOptions> configure)
        {
            var options = new InertiaOptions();
            configure(options);
            return options;
        }
    }

    [Fact]
    public void Big_integers_and_string_numbers_follow_the_options()
    {
        var ts = Generate(
            o =>
            {
                o.PreserveBigIntegers = true;
                o.TypeScript.Page<OrdersPage>("Orders");
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = TypeScriptJsonContext.Default, NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString });
        Assert.Contains("id: number | bigint | string", ts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_file_is_written_in_development_only_when_it_changed()
    {
        var path = Path.Combine(_root.Path, "ClientApp/types/inertia.d.ts");
        var harness = new Harness(o => o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts", contentRoot: _root.Path, environment: "Development");
        var writer = harness.Services.GetServices<IHostedService>().OfType<InertiaTypeScriptWriter>().Single();

        await writer.StartAsync(CancellationToken.None);
        Assert.Contains("export interface InertiaPages", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddHours(-1));
        await writer.StartAsync(CancellationToken.None);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(path));

        var production = new Harness(o => o.TypeScript.OutputPath = "ClientApp/types/other.d.ts", contentRoot: _root.Path);
        await production.Services.GetServices<IHostedService>().OfType<InertiaTypeScriptWriter>().Single().StartAsync(CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_root.Path, "ClientApp/types/other.d.ts")));
    }

    [Fact]
    public async Task The_generate_flag_writes_in_any_environment_and_fails_without_an_output_path()
    {
        var path = Path.Combine(_root.Path, "ClientApp/types/ci.d.ts");
        var harness = new Harness(o => o.TypeScript.OutputPath = "ClientApp/types/ci.d.ts", contentRoot: _root.Path);
        var exitCode = Environment.ExitCode;
        try
        {
            await Writer(harness).StartAsync(CancellationToken.None);
            Assert.True(File.Exists(path));
            Assert.Equal(exitCode, Environment.ExitCode);

            await Writer(new Harness(contentRoot: _root.Path)).StartAsync(CancellationToken.None);
            Assert.Equal(1, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = exitCode;
        }

        static InertiaTypeScriptWriter Writer(Harness harness) => new(
            harness.Services.GetRequiredService<IOptions<InertiaOptions>>(),
            harness.Services.GetRequiredService<IHostEnvironment>(),
            harness.Services.GetRequiredService<InertiaPageWriter>(),
            harness.Services.GetRequiredService<ILogger<InertiaTypeScriptWriter>>()) { GenerateOnly = true };
    }

    [Fact]
    public void Member_number_handling_overrides_type_and_global_settings()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = TypeScriptJsonContext.Default,
            NumberHandling = JsonNumberHandling.WriteAsString,
        };
        var ts = Generate(o => o.TypeScript.Page<TsSerializationPage>("Serialized"), json);
        Assert.Contains("number: number | string", ts, StringComparison.Ordinal);
        Assert.Contains("strict: number\n", ts, StringComparison.Ordinal);
        Assert.Contains("values: (number | string)[]", ts, StringComparison.Ordinal);
        Assert.Contains("token: unknown", ts, StringComparison.Ordinal);
        var serialized = JsonSerializer.Serialize(new TsSerializationPage(42, 7, [1], "secret"), json);
        Assert.Contains("\"number\":\"42\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"strict\":7", serialized, StringComparison.Ordinal);
        Assert.Contains("\"values\":[\"1\"]", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_converters_on_members_and_primitives_produce_unknown_with_a_warning()
    {
        var options = new InertiaOptions();
        options.TypeScript.Page<TsSerializationPage>("Serialized");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = TypeScriptJsonContext.Default };
        json.Converters.Add(new MemberMaskConverter());
        var generator = new TypeScriptGenerator(json, options);
        var ts = generator.Generate();
        Assert.Contains("token: unknown", ts, StringComparison.Ordinal);
        Assert.Contains(generator.Warnings, warning => warning.Contains("custom JSON converter", StringComparison.Ordinal));
        Assert.Equal("unknown", generator.Reference(typeof(string)));
        var nullableJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = TypeScriptJsonContext.Default };
        nullableJson.Converters.Add(new TsNullableNumberConverter());
        var nullableGenerator = new TypeScriptGenerator(nullableJson, options);
        Assert.Equal("unknown", nullableGenerator.Reference(typeof(int?)));
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<Status>))]
public enum Status
{
    [JsonStringEnumMemberName("active")]
    Active,

    [JsonStringEnumMemberName("disabled")]
    Disabled,
}

public enum Priority
{
    Low,
    Normal,
    High,
}

public sealed record TsUser(int Id, string Name, Status Status, DateTime CreatedAt, double? Score);

public sealed record Paged<T>(List<T> Items, int Page);

public sealed record UsersPage(
    List<TsUser> Users,
    int Total,
    string? Filter,
    InertiaProp Stats,
    [property: InertiaPropType(typeof(List<TsUser>))] InertiaProp Admins,
    Dictionary<string, List<TsUser>> ByRole,
    [property: JsonPropertyName("display-name")] string DisplayName);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
public abstract record Shape;

public sealed record Circle(double Radius) : Shape;

public sealed record Square(double Side) : Shape;

public sealed record OrdersPage(long Id, Priority Priority, Shape Shape, Paged<TsUser> Lines);

public sealed record Flash(string? Success);

public sealed record Unregistered(int X);

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
public sealed record TsSerializationPage(
    int Number,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int Strict,
    List<int> Values,
    [property: JsonConverter(typeof(MemberMaskConverter))] string Token);

public sealed class TsNullableNumberConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => null;

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options) => writer.WriteStringValue("custom nullable number");
}

#if NET11_0_OR_GREATER
public union TsSubject(TsUser, string);

public sealed record UnionPage(TsSubject Subject);

[JsonSerializable(typeof(UnionPage))]
#endif
[JsonSerializable(typeof(UsersPage))]
[JsonSerializable(typeof(OrdersPage))]
[JsonSerializable(typeof(Flash))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(TsSerializationPage))]
[JsonSerializable(typeof(int?))]
internal sealed partial class TypeScriptJsonContext : JsonSerializerContext;
