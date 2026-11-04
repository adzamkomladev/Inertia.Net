"""Patch an isolated checkout after its React SSR build; never run in the release checkout."""
from pathlib import Path
import sys
sample = Path(sys.argv[1]) / "samples/MinimalApi.React"
p = sample / "Program.cs"
s = p.read_text().replace('o.Ssr.BundlePath = "ssr/ssr.js";', '''o.Ssr.BundlePath = "ssr/ssr.js";
    o.Ssr.Cache.Duration = TimeSpan.FromSeconds(1);
    o.Ssr.Cache.StaleFor = TimeSpan.FromMinutes(1);
    o.Ssr.Timeout = TimeSpan.FromSeconds(15);''')
s = s.replace('app.UseResponseCompression();', '''app.Use(async (http, next) =>
{
    http.Response.Headers["X-Probe-Connection"] = http.Connection.Id;
    http.Response.Headers["X-Probe-Protocol"] = http.Request.Protocol;
    await next(http);
});
app.MapGet("/phase6", () => Results.Content("<html><body><h1>Stale SSR refresh connection probe</h1><pre id='results'>Running...</pre></body></html>", "text/html"));
app.MapGet("/phase6/ping", () => Results.Text("pong"));
app.UseResponseCompression();''')
p.write_text(s)
p = sample / "ssr/ssr.js"
s = p.read_text()
assert '"port": 13994' in s, "Build SSR with INERTIA_SSR_PORT=13994 first"
s = 'import fs from "node:fs";\nlet probeCall=0;\n' + s
s = s.replace('var renderPage = async (page) => {', '''var renderPage = async (page) => {
    const call=++probeCall;
    fs.appendFileSync("/tmp/inertia-phase6-renderer.jsonl", JSON.stringify({call,event:"start",epochMs:Date.now(),component:page.component})+"\\n");
    await new Promise(resolve => setTimeout(resolve,4000));
    fs.appendFileSync("/tmp/inertia-phase6-renderer.jsonl", JSON.stringify({call,event:"end",epochMs:Date.now()})+"\\n");''')
p.write_text(s)
Path('/tmp/inertia-phase6-renderer.jsonl').unlink(missing_ok=True)
