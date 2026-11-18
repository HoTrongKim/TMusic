import test from 'node:test';
import assert from 'node:assert/strict';
import gateway from '../src/gateway.mjs';

test('reports unavailable when the tunnel origin is not configured', async () => {
  const response = await gateway.fetch(new Request('https://example.workers.dev/api/v1/albums'), {}, {});
  assert.equal(response.status, 503);
});

test('rejects tunnel origins outside the permitted Cloudflare quick tunnel', async () => {
  for (const origin of ['http://example.trycloudflare.com', 'https://evil.example', 'https://example.trycloudflare.com.evil.example', 'https://example.trycloudflare.com/path']) {
    const response = await gateway.fetch(new Request('https://example.workers.dev/health'), { TMUSIC_UPSTREAM: origin }, {});
    assert.equal(response.status, 503);
  }
});

test('forwards path, query, method and body without following redirects', async () => {
  const previousFetch = globalThis.fetch;
  let forwarded;
  globalThis.fetch = async request => { forwarded = request; return new Response('ok'); };
  try {
    const request = new Request('https://example.workers.dev/api/v1/songs?limit=2', { method: 'POST', body: 'payload', headers: { 'content-type': 'text/plain' } });
    const response = await gateway.fetch(request, { TMUSIC_UPSTREAM: 'https://abc-def.trycloudflare.com' }, {});
    assert.equal(response.status, 200);
    assert.equal(forwarded.url, 'https://abc-def.trycloudflare.com/api/v1/songs?limit=2');
    assert.equal(forwarded.method, 'POST');
    assert.equal(await forwarded.text(), 'payload');
    assert.equal(forwarded.redirect, 'manual');
  } finally { globalThis.fetch = previousFetch; }
});

test('a path beginning with two slashes cannot change the destination host', async () => {
  const previousFetch = globalThis.fetch;
  let forwarded;
  globalThis.fetch = async request => { forwarded = request; return new Response('ok'); };
  try {
    await gateway.fetch(new Request('https://example.workers.dev//evil.example/path'), { TMUSIC_UPSTREAM: 'https://abc-def.trycloudflare.com' }, {});
    assert.equal(new URL(forwarded.url).host, 'abc-def.trycloudflare.com');
  } finally { globalThis.fetch = previousFetch; }
});
