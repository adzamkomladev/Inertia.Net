using System.Diagnostics;
using System.Text;
using Inertia.Net.Mvc;
using Microsoft.AspNetCore.Http;

namespace Inertia.Net.Benchmarks;

/// <summary>
/// The <c>&lt;inertia /&gt;</c> tag helper output without SSR: the 1.1 string path (<c>GetBodyHtml()</c>, then Razor writes the string)
/// against the streaming <c>IHtmlContent</c>, both written to a <see cref="StreamWriter"/> the way Razor's response writer encodes them.
/// </summary>
internal static class MvcProbe
{
    internal static void Run()
    {
        foreach (var size in new[] { 2 * 1024, 1024 * 1024 })
        {
            var json = Encoding.UTF8.GetBytes("{\"props\":{\"text\":\"" + new string('x', size) + "\"}}");
            var context = new InertiaRootViewContext(new DefaultHttpContext(), "Page", "App", InertiaRootViewContext.EmptyViewData, json, "app");
            var iterations = size > 100_000 ? 200 : 20_000;
            for (var repetition = 1; repetition <= 3; repetition++)
            {
                foreach (var (name, write) in new (string, Action<TextWriter>)[]
                {
                    ("string", w => w.Write(context.GetBodyHtml())),
                    ("stream", w => new PageBodyHtmlContent(context).WriteTo(w, System.Text.Encodings.Web.HtmlEncoder.Default)),
                })
                {
                    using var writer = new StreamWriter(Stream.Null, new UTF8Encoding(false), 16 * 1024);
                    for (var i = 0; i < 20; i++) write(writer);
                    var before = GC.GetTotalAllocatedBytes(true);
                    var started = Stopwatch.GetTimestamp();
                    for (var i = 0; i < iterations; i++) write(writer);
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    Console.WriteLine($"run={repetition} {size / 1024} KiB {name}: {(GC.GetTotalAllocatedBytes(true) - before) / iterations} B/render, {elapsed.TotalMicroseconds / iterations:F1} us/render");
                }
            }
        }
    }
}
