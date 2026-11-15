// 本地跑一个「假 Supabase + 中转」，用于客户端联调：
//   node relay/test/serve.mjs
// 然后：RxNeL.exe --relay http://127.0.0.1:8800 relay-token-123
import http from "node:http";
import { createRequire } from "node:module";

process.env.SUPABASE_URL = "http://127.0.0.1:8799";
process.env.SUPABASE_ANON_KEY = "test-anon-key-0123456789";
process.env.RELAY_TOKEN = "relay-token-123";

const require = createRequire(import.meta.url);
const handler = require("../api/[...path].js");

let inserted = [];

http.createServer((req, res) => {
  let body = "";

  req.on("data", c => (body += c));
  req.on("end", () => {
    if (req.url.startsWith("/auth/v1/token")) {
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({
        access_token: "mock-token",
        refresh_token: "mock-refresh",
        user: { id: "11111111-1111-1111-1111-111111111111", email: "mock@example.com", email_confirmed_at: "2026-01-01T00:00:00Z" }
      }));
      return;
    }

    if (req.url.startsWith("/auth/v1/settings")) {
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({ disable_signup: false }));
      return;
    }

    if (req.url.startsWith("/rest/v1/rx_profiles")) {
      if (req.method === "POST") {
        inserted.push(body);
        res.writeHead(201, { "content-type": "application/json" });
        res.end(body || "[]");
        return;
      }

      res.writeHead(200, { "content-type": "application/json" });
      res.end("[]");
      return;
    }

    res.writeHead(404, { "content-type": "application/json" });
    res.end("{}");
  });
}).listen(8799, "127.0.0.1");

http.createServer(async (req, res) => {
  res.status = code => { res.statusCode = code; return res; };
  res.json = p => { res.setHeader("content-type", "application/json"); res.end(JSON.stringify(p)); return res; };
  res.send = p => { res.end(p); return res; };

  await handler(req, res);

  if (req.url.startsWith("/rest/v1/rx_profiles") && req.method === "POST") {
    console.log("inserted:", inserted.at(-1));
  }
}).listen(8800, "127.0.0.1", () => {
  console.log("relay → http://127.0.0.1:8800 (token: relay-token-123, upstream mock 8799)");
});
