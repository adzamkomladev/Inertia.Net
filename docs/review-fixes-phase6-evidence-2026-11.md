# Phase 6: SSR browser trace and upstream drift (2026-11-04)

The browser probe passed: a stale SSR hit and its next request reused one HTTP/1.1 connection while the real React SSR renderer
was still refreshing. This memo covers plan 6.3 and 6.5; sample suites, login screenshots and final AOT/release gates are recorded
separately in the execution record.

## Browser trace

Isolated checkout: `f618bee167206c7fe215ee3ecd9fe0144e719320`, detached at `/tmp/inertia-v163-phase6-probe`.
App: `127.0.0.1:5294`; managed Node SSR: `127.0.0.1:13994`. The release checkout and its servers on 5101/13863 were untouched.
The probe set `Ssr.Cache.Duration = 1 second`, `StaleFor = 1 minute`, and the render deadline to 15 seconds. Its generated SSR
entry awaited an artificial four-second delay before invoking the sample's real React renderer and logged start/end epochs.
Instrumentation added only two temporary routes and connection/protocol response headers in the isolated sample.

| Browser request | Duration | Result |
| --- | ---: | --- |
| Cold HTML `/` | 4,305.1 ms | 200, server-rendered HTML |
| Same HTML after 1.5 seconds | 18.4 ms | 200, stale server-rendered HTML |
| `/phase6/ping` after another 200 ms | 18.6 ms | 200, `pong`, refresh still running |

All three used Kestrel connection `0HNPQHRCFCJU9` and Chromium connection **21**, `http/1.1`; Chromium marked each fetch as reused.
Renderer call 2 ran from epoch `1793808937389` through `1793808941393` (4,004 ms). The ping ran from `1793808937583` through
`1793808937602`, finishing 3,791 ms before the renderer's delay ended. Assertions verify these inequalities, response statuses,
actual SSR markup, and both independent connection identities.

Artifacts under [evidence/phase6-2026-11](evidence/phase6-2026-11):

- [Compressed Chromium performance trace](evidence/phase6-2026-11/chromium-trace.json.gz): decompress with `gzip -dk` and load the
  JSON in Chrome DevTools Performance. User Timing contains `cold SSR miss`, `stale SSR hit`, and `ping during refresh`.
- [Summary](evidence/phase6-2026-11/summary.json): browser/server connection IDs, network timings and renderer events.
- [Timing screenshot](evidence/phase6-2026-11/timings.png), [environment and binary hashes](evidence/phase6-2026-11/environment.json),
  [browser capture](evidence/phase6-2026-11/capture.cjs), and [isolated instrumentation](evidence/phase6-2026-11/prepare.py).

Commands used (SDK path on this host is `/root/.dotnet`; cached sample dependencies were reused):

```sh
# From the release checkout; choose a fresh path if this temporary checkout already exists.
phase6_release="$PWD"
git worktree add --detach /tmp/inertia-v163-phase6-probe f618bee167206c7fe215ee3ecd9fe0144e719320
ln -s "$phase6_release/samples/MinimalApi.React/node_modules" /tmp/inertia-v163-phase6-probe/samples/MinimalApi.React/node_modules
cp -a "$phase6_release/samples/MinimalApi.React/wwwroot/build" /tmp/inertia-v163-phase6-probe/samples/MinimalApi.React/wwwroot/build
# The fetch-only harness never executes those copied client assets.
cd /tmp/inertia-v163-phase6-probe/samples/MinimalApi.React
INERTIA_SSR_PORT=13994 ./node_modules/.bin/vite build --ssr
# From the release checkout:
python3 docs/evidence/phase6-2026-11/prepare.py /tmp/inertia-v163-phase6-probe
# From the isolated checkout (leave running until capture finishes):
INERTIA_SSR=1 INERTIA_SSR_PORT=13994 ASPNETCORE_ENVIRONMENT=Production \
  /root/.dotnet/dotnet run --project samples/MinimalApi.React -c Release \
  --no-launch-profile --urls http://127.0.0.1:5294
# From the release checkout, after startup/SSR health succeeds:
node docs/evidence/phase6-2026-11/capture.cjs
```

The actual preparation was applied directly before capture; `prepare.py` preserves those edits for reproduction. Vite SSR build
exited 0; browser capture and all its assertions exited 0. The sample built and served successfully; `node --check` and Python
syntax compilation of the saved scripts exited 0. Gzip/JSON decoding confirms that the trace contains all three timing markers.
Chromium was 156.0.8078.4, Node 26.13.0 and .NET SDK 10.0.403 on Linux x64. Browser-summary headers are restricted to probe and
content-type fields; the performance trace contains no cookie header values.

Limitations: one loopback Chromium run, no CPU/network throttling, no latency distribution or throughput claim. A tiny browser
harness fetches real sample SSR HTML without executing its client scripts, avoiding unrelated asset/live-transport connections.
The ping follows the stale hit by 200 ms to put it inside the renderer's recorded delay; its same-socket identity is verified,
not assumed. This proves HTTP/1.1 connection availability; it does not measure HTTP/2 or HTTP/3, queue saturation or cancellation.
The existing gated Kestrel regression independently covers the same ordering. No shipped behavior was changed by this probe.

## Upstream snapshot and follow-ups

Fetched the current `inertiajs/inertia` `3.x` head through the GitHub API on 2026-11-04:
`845e7d7b4f772aa1f2967091339d1cbffbbc76e2` (upstream author date 2026-11-02). Downloaded the three files by that immutable commit,
decoded the API bodies and verified their Git blob hashes. [Source hashes and URLs](evidence/phase6-2026-11/upstream-sources.json)
also identify the official DevTools protocol Markdown fetched that day. These are source-review follow-ups, not runtime
conformance results or a proposal to expand this release.

1. **Describe client request construction.** `reset` joins `only` in Partial-Data and also has its own Reset header; either makes
   the visit partial. No incompatible rename was found for the partial/error-bag headers already listed locally.
   [RequestParams](https://github.com/inertiajs/inertia/blob/845e7d7b4f772aa1f2967091339d1cbffbbc76e2/packages/core/src/requestParams.ts).
2. **Assess QUERY visits and client response rules.** The client supports body-bearing QUERY visits and derives `queryMethodData`
   locally; this is absent from `docs/protocol.md`, not a requirement to add a server-emitted page field. Also document asynchronous
   version-change handling, error preservation on background partials, and deferred-request flash preservation. Existing local
   redirect/version/merge headers remain recognizable; these omissions need a scoped client/adapter conformance review.
   [Response](https://github.com/inertiajs/inertia/blob/845e7d7b4f772aa1f2967091339d1cbffbbc76e2/packages/core/src/response.ts),
   [request envelope](https://github.com/inertiajs/inertia/blob/845e7d7b4f772aa1f2967091339d1cbffbbc76e2/packages/core/src/request.ts).
3. **Add a DevTools protocol reference and review three gaps.** `docs/protocol.md` currently has no DevTools section. The official
   contract specifies subpath discovery through `X-Inertia-Devtools-Base-Path` and a matching discovery-script attribute; neither
   exists in the recorder. It requires absolute `__meta.url`, while the recorder writes path plus query. Its optional list endpoint
   returns metadata summaries, while the local list currently returns complete entries. Review these separately with subpath and
   entry-shape tests. Discovery ID, parent-out correlation, request-type precedence, read authorization and bounded storage are
   present; missing optional source locations are allowed.
   [Official DevTools protocol](https://inertiajs.com/docs/v3/advanced/devtools-protocol),
   [local recorder](../src/Inertia.Net/InertiaDevTools.cs).

No protocol or runtime implementation was modified during this drift check.
