function tunnelOrigin(raw) {
  try {
    const url = new URL(raw);
    if (url.protocol !== 'https:' || url.username || url.password || url.port ||
        url.pathname !== '/' || url.search || url.hash ||
        !/^[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com$/.test(url.hostname)) return null;
    return url.origin;
  } catch {
    return null;
  }
}

export default {
  async fetch(request, env) {
    const origin = tunnelOrigin(env.TMUSIC_UPSTREAM);
    if (!origin) return new Response('TMusic tạm thời chưa kết nối được máy chủ.', { status: 503 });
    const incoming = new URL(request.url);
    const destination = new URL(origin);
    destination.pathname = incoming.pathname;
    destination.search = incoming.search;
    const headers = new Headers(request.headers);
    headers.delete('host');
    const forwarded = new Request(new Request(destination, request), { headers, redirect: 'manual' });
    return fetch(forwarded);
  },
};
