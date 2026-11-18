// Vercel Function：把客户端的 /auth/* 和 /rest/* 转发到 Supabase。
// 目的：客户端连 https://<你的域名>.vercel.app（未被墙），由它去访问 *.supabase.co（服务端到服务端不受 SNI 封锁影响）。

// 注意：这些值必须每次请求读取，Vercel 冷启动和本地测试才会拿到最新环境变量
function config() {
  return {
    url: (process.env.SUPABASE_URL || "").replace(/\/$/, ""),
    key: process.env.SUPABASE_ANON_KEY || "",
    token: process.env.RELAY_TOKEN || ""
  };
}

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

module.exports = async function handler(req, res) {
  const { url: SUPABASE_URL, key: SUPABASE_ANON_KEY, token: RELAY_TOKEN } = config();

  if (!SUPABASE_URL || !SUPABASE_ANON_KEY) {
    res.status(500).json({ error: "relay_not_configured", message: "缺少 SUPABASE_URL / SUPABASE_ANON_KEY 环境变量" });
    return;
  }

  if (RELAY_TOKEN && req.headers["x-relay-token"] !== RELAY_TOKEN) {
    res.status(401).json({ error: "unauthorized", message: "缺少或错误的 x-relay-token" });
    return;
  }

  // vercel.json 的 rewrite 会把 /auth/* 转成 /api/auth/*，这里去掉 /api 前缀
  let path = req.url || "/";

  if (path.startsWith("/api/")) path = path.slice(4);

  // 只允许 /auth 与 /rest 两个前缀，避免变成任意代理

  if (!path.startsWith("/auth/") && !path.startsWith("/rest/")) {
    res.status(404).json({ error: "not_found", message: "只转发 /auth/* 与 /rest/*" });
    return;
  }

  const target = SUPABASE_URL + path;

  const headers = { apikey: SUPABASE_ANON_KEY };

  for (const name of PASS_REQUEST_HEADERS) {
    const value = req.headers[name];
    if (value) headers[name] = value;
  }

  // 客户端自带的 apikey（可能是老版本）不允许覆盖 relay 的配置
  if (req.headers["apikey"]) headers["apikey"] = SUPABASE_ANON_KEY;

  let body;

  if (req.method !== "GET" && req.method !== "HEAD") {
    body = await readBody(req);
  }

  let upstream;

  try {
    upstream = await fetch(target, {
      method: req.method,
      headers,
      body,
      redirect: "manual"
    });
  } catch (error) {
    res.status(502).json({ error: "upstream_unreachable", message: String(error) });
    return;
  }

  const text = await upstream.text();

  res.status(upstream.status);

  for (const name of PASS_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value) res.setHeader(name, value);
  }

  res.setHeader("cache-control", "no-store");
  res.send(text);
};

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];

    req.on("data", chunk => chunks.push(chunk));
    req.on("end", () => resolve(Buffer.concat(chunks)));
    req.on("error", reject);
  });
}
