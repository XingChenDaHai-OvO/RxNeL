// 本地自测：起一个假 Supabase + 一个假的 Vercel 运行时，验证中转的转发/拦截逻辑。
// 用法：node relay/test/local-test.mjs
import http from "node:http";
import { createRequire } from "node:module";

process.env.SUPABASE_URL = "http://127.0.0.1:8799";
process.env.SUPABASE_ANON_KEY = "test-anon-key-0123456789";
process.env.RELAY_TOKEN = "";

const require = createRequire(import.meta.url);
const handler = require("../api/[...path].js");

let upstreamHits = [];

const upstream = http.createServer((req, res) => {
  upstreamHits.push({ url: req.url, apikey: req.headers["apikey"] || "", auth: req.headers["authorization"] || "" });

  if (req.url.startsWith("/auth/v1/settings")) {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ external: {}, disable_signup: false }));
    return;
  }

  if (req.url.startsWith("/rest/v1/rx_profiles")) {
    res.writeHead(200, { "content-type": "application/json" });
    res.end("[]");
    return;
  }

  res.writeHead(404, { "content-type": "application/json" });
  res.end(JSON.stringify({ error: "upstream 404" }));
});

const listen = (server, port) =>
  new Promise(resolve => server.listen(port, "127.0.0.1", () => resolve()));

await listen(upstream, 8799);

const relay = http.createServer(async (req, res) => {
  res.status = code => {
    res.statusCode = code;
    return res;
  };
  res.json = payload => {
    res.setHeader("content-type", "application/json");
    res.end(JSON.stringify(payload));
    return res;
  };
  res.send = payload => {
    res.end(payload);
    return res;
  };

  await handler(req, res);
});

await listen(relay, 8800);

const call = (path, headers = {}) =>
  fetch(`http://127.0.0.1:8800${path}`, { headers }).then(async r => ({
    status: r.status,
    body: await r.text()
  }));

const results = [];

results.push(["auth 转发", await call("/auth/v1/settings")]);
results.push(["rest 转发", await call("/rest/v1/rx_profiles?select=id&limit=1")]);
results.push(["非法前缀拦截", await call("/evil/path")]);

// 打开 token 保护再测
process.env.RELAY_TOKEN = "secret-token";
results.push(["缺 token 被拒", await call("/auth/v1/settings")]);
results.push(["带 token 通过", await call("/auth/v1/settings", { "x-relay-token": "secret-token" })]);

for (const [name, r] of results) {
  console.log(`${name}: ${r.status} ${r.body.slice(0, 80)}`);
}

console.log("upstream 收到的 apikey:", upstreamHits.map(h => h.apikey).join(" | "));
console.log("路径记录:", upstreamHits.map(h => h.url).join(" | "));

upstream.close();
relay.close();
