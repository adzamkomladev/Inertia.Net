using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MemoryPack;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Inertia.Net.Benchmarks;

/// <summary>Includes JSON conversion for arbitrary flash values; only the state envelope uses MemoryPack.</summary>
[MemoryDiagnoser]
public class StateBenchmarks
{
    [Params(0, 1024, 16384)] public int FlashBytes { get; set; }
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { TypeInfoResolver = InertiaJsonContext.Default };
    private readonly IDataProtector _protector = new EphemeralDataProtectionProvider().CreateProtector("Inertia.Net.State");
    private InertiaFeature _feature = null!;
    private byte[] _json = null!;
    private byte[] _binary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _feature = new InertiaFeature().Flash("message", new string('x', FlashBytes)).ClearHistory();
        for (var i = 0; i < 20; i++)
        {
            _feature.RestoreFlash($"saved{i}", i);
            _feature.RestoreError("default", $"field{i}", ["Required"]);
        }
        _feature.WithErrors(new Dictionary<string, string> { ["field0"] = "Pending" });
        _json = Json();
        _binary = Binary();
        var restored = RestoreBinary();
        using var actual = JsonDocument.Parse(InertiaStateSerializer.Serialize(restored, _options));
        using var expected = JsonDocument.Parse(_json);
        if (!JsonElement.DeepEquals(actual.RootElement, expected.RootElement)) throw new InvalidOperationException("State codecs differ.");
        Console.WriteLine($"State bytes (flash={FlashBytes}): JSON={_json.Length}, MemoryPack={_binary.Length}; protected Base64URL={Protect(_json).Length}/{Protect(_binary).Length}");
    }

    [Benchmark(Baseline = true)] public byte[] Json() => InertiaStateSerializer.Serialize(_feature, _options);
    [Benchmark]
    public byte[] Binary() => MemoryPackSerializer.Serialize(new StateEnvelope
    {
        Errors = _feature.Errors,
        Flash = _feature.FlashData!.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToUtf8Bytes(p.Value, _options)),
        ClearHistory = _feature.HistoryCleared,
        PreserveFragment = _feature.FragmentPreserved,
    });
    [Benchmark]
    public InertiaFeature RestoreJson()
    {
        var feature = new InertiaFeature();
        if (!InertiaStateSerializer.TryRestore(feature, _json)) throw new InvalidOperationException("Invalid state.");
        return feature;
    }
    [Benchmark]
    public InertiaFeature RestoreBinary()
    {
        var state = MemoryPackSerializer.Deserialize<StateEnvelope>(_binary)!;
        var feature = new InertiaFeature();
        foreach (var (bag, fields) in state.Errors ?? [])
            foreach (var (field, messages) in fields) feature.RestoreError(bag, field, messages);
        foreach (var (key, value) in state.Flash ?? [])
        {
            using var document = JsonDocument.Parse(value);
            feature.RestoreFlash(key, document.RootElement.Clone());
        }
        feature.RestoreFlags(state.ClearHistory, state.PreserveFragment);
        return feature;
    }
    [Benchmark] public string JsonCookie() => Protect(Json());
    [Benchmark] public string BinaryCookie() => Protect(Binary());
    [Benchmark] public bool StateChecks() => _feature.HasState && ReferenceEquals(_feature.FlashData, _feature.FlashData);
    private string Protect(byte[] bytes) => WebEncoders.Base64UrlEncode(_protector.Protect(bytes));
}

[MemoryPackable]
public partial class StateEnvelope
{
    public Dictionary<string, Dictionary<string, string[]>>? Errors { get; set; }
    public Dictionary<string, byte[]>? Flash { get; set; }
    public bool ClearHistory { get; set; }
    public bool PreserveFragment { get; set; }
}
