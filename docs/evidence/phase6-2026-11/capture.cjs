const fs = require('node:fs');
const zlib = require('node:zlib');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || '../../../tests/e2e/node_modules/playwright');
(async () => {
  const out = process.env.PROBE_OUTPUT || __dirname;
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  const network = [];
  await cdp.send('Network.enable');
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });
  cdp.on('Network.responseReceived', e => {
    if (e.response.url.startsWith('http://127.0.0.1:5294/')) network.push({
      requestId: e.requestId, timestamp: e.timestamp, url: e.response.url,
      protocol: e.response.protocol, connectionId: e.response.connectionId,
      connectionReused: e.response.connectionReused, timing: e.response.timing,
      headers: Object.fromEntries(Object.entries(e.response.headers).filter(([key])=>
        ['x-probe-connection','x-probe-protocol','content-type'].includes(key.toLowerCase()))),
    });
  });
  await cdp.send('Tracing.start', { categories: 'devtools.timeline,blink.user_timing,loading', transferMode: 'ReturnAsStream' });
  await page.goto('http://127.0.0.1:5294/phase6');
  const requests = await page.evaluate(async () => {
    const rows = [];
    async function request(label, url) {
      performance.mark(label + ':start');
      const epochStartMs = Date.now(), start = performance.now();
      const response = await fetch(url, { cache: 'no-store' });
      const body = await response.text();
      const row = { label, url, epochStartMs, epochEndMs: Date.now(), durationMs: performance.now()-start,
        status: response.status, connection: response.headers.get('x-probe-connection'),
        protocol: response.headers.get('x-probe-protocol'), serverRendered: body.includes('data-server-rendered="true"'),
        pong: body === 'pong' };
      performance.mark(label + ':end');
      performance.measure(label, label + ':start', label + ':end');
      rows.push(row);
    }
    await request('cold SSR miss', '/');
    await new Promise(resolve => setTimeout(resolve, 1500));
    await request('stale SSR hit', '/');
    await new Promise(resolve => setTimeout(resolve, 200));
    await request('ping during refresh', '/phase6/ping');
    document.querySelector('#results').textContent = JSON.stringify(rows, null, 2);
    return rows;
  });
  // Let the deliberately slow background refresh finish; no request is sent during this wait.
  await page.waitForTimeout(4500);
  const renderer = fs.readFileSync('/tmp/inertia-phase6-renderer.jsonl','utf8').trim().split('\n').map(JSON.parse);
  assert.equal(requests.length, 3);
  for (const row of requests) { assert.equal(row.status,200); assert.equal(row.protocol,'HTTP/1.1'); }
  assert(requests[0].serverRendered && requests[1].serverRendered);
  assert(requests[2].pong);
  assert.equal(new Set(requests.map(r=>r.connection)).size,1);
  assert(requests[0].durationMs >= 4000);
  assert(requests[1].durationMs < 1000 && requests[2].durationMs < 1000);
  const start = renderer.find(r=>r.call===2 && r.event==='start');
  const end = renderer.find(r=>r.call===2 && r.event==='end');
  assert(start && end);
  assert(start.epochMs < requests[2].epochStartMs && end.epochMs > requests[2].epochEndMs);
  const targetNetwork = network.filter(r=>r.url.endsWith('/') || r.url.endsWith('/phase6/ping'));
  assert.equal(new Set(targetNetwork.map(r=>r.connectionId)).size,1);
  assert(targetNetwork.every(r=>r.protocol==='http/1.1'));
  const result = { browser: browser.version(), capturedUtc: new Date().toISOString(), requests, renderer, network,
    assertion: 'PASS: stale hit and ping reused the same HTTP/1.1 connection while SSR refresh call 2 was still running' };
  fs.writeFileSync(path.join(out,'summary.json'),JSON.stringify(result,null,2)+'\n');
  await page.screenshot({ path: path.join(out,'timings.png'), fullPage:true });
  const completed = new Promise(resolve=>cdp.once('Tracing.tracingComplete',resolve));
  await cdp.send('Tracing.end');
  const { stream } = await completed;
  const chunks=[];
  for (;;) {
    const item=await cdp.send('IO.read',{handle:stream});
    chunks.push(Buffer.from(item.data,item.base64Encoded?'base64':'utf8'));
    if(item.eof) break;
  }
  await cdp.send('IO.close',{handle:stream});
  fs.writeFileSync(path.join(out,'chromium-trace.json.gz'),zlib.gzipSync(Buffer.concat(chunks)));
  console.log(JSON.stringify(result,null,2));
  await browser.close();
})().catch(e=>{ console.error(e); process.exit(1); });
