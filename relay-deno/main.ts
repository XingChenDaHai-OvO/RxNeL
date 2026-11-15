// Deno Deploy 版中转：把 /auth/* 与 /rest/* 转发到 Supabase。
// 为什么不用 Vercel：*.vercel.app 在部分网络（含国内多数线路）SNI 被阻断，实测 TLS 握手卡住；
// *.deno.dev 实测可达。
//
// 部署：dash.deno.com → New Project → Deploy from GitHub → 选本仓库
//       Entrypoint: relay-deno/main.ts
//       Environment Variables: SUPABASE_URL / SUPABASE_ANON_KEY / RELAY_TOKEN(可选)

const PASS_REQUEST_HEADERS = [
  "authorization",
  "content-type",
  "accept",
  "prefer",
  "range",
  "x-client-info",
  "x-supabase-api-version",
  "x-upsert"
];

const PASS_RESPONSE_HEADERS = [
  "content-type",
  "content-range",
  "content-profile",
  "preference-applied",
  "location",
  "x-csrf-token",
  "retry-after"
];

function config() {
  return {
    url: (Deno.env.get("SUPABASE_URL") ?? "").replace(/\/$/, ""),
    key: Deno.env.get("SUPABASE_ANON_KEY") ?? "",
    token: Deno.env.get("RELAY_TOKEN") ?? ""
  };
}

Deno.serve(async (req: Request) => {
  const { url: supabaseUrl, key: supabaseKey, token: relayToken } = config();
  const url = new URL(req.url);
  const path = url.pathname + url.search;

  if (!supabaseUrl || !supabaseKey) {
    return json(500, { error: "relay_not_configured", message: "缺少 SUPABASE_URL / SUPABASE_ANON_KEY" });
  }

  if (relayToken && req.headers.get("x-relay-token") !== relayToken) {
    return json(401, { error: "unauthorized", message: "缺少或错误的 x-relay-token" });
  }

  if (!path.startsWith("/auth/") && !path.startsWith("/rest/")) {
    return json(404, { error: "not_found", message: "只转发 /auth/* 与 /rest/*" });
  }

  const headers = new Headers();
  headers.set("apikey", supabaseKey);

  for (const name of PASS_REQUEST_HEADERS) {
    const value = req.headers.get(name);
    if (value) headers.set(name, value);
  }

  let body: ArrayBuffer | undefined;

  if (req.method !== "GET" && req.method !== "HEAD") {
    body = await req.arrayBuffer();
  }

  let upstream: Response;

  try {
    upstream = await fetch(supabaseUrl + path, {
      method: req.method,
      headers,
      body,
      redirect: "manual"
    });
  } catch (error) {
    return json(502, { error: "upstream_unreachable", message: String(error) });
  }

  const text = await upstream.text();
  const responseHeaders = new Headers();

  for (const name of PASS_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) responseHeaders.set(name, value);
  }

  responseHeaders.set("cache-control", "no-store");

  return new Response(text, { status: upstream.status, headers: responseHeaders });
});

function json(status: number, payload: unknown): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "content-type": "application/json" }
  });
}
