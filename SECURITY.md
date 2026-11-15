# 安全说明

## 本仓库不包含任何密钥

`relay/`、`relay-netlify/`、`relay-deno/` 都从**环境变量**读取配置：

```
SUPABASE_URL
SUPABASE_ANON_KEY
RELAY_TOKEN        （可选，建议设置）
```

客户端不保存 anon key：启动器只需要「中转地址 + 中转令牌」，
anon key 只存在于托管平台的环境变量里。

## 如果你 fork 了本项目

1. 自己建一个 Supabase 项目，执行 `db/profiles.sql`
2. 自己部署一个中转（Netlify / Deno Deploy），配置上面三个环境变量
3. 启动器 → 设置 → 通用 → 云后端：填中转地址 + 令牌

## 客户端本地数据

| 文件 | 内容 | 保护方式 |
|---|---|---|
| `%LOCALAPPDATA%\RbxLite\accounts.json` | Roblox 账号 + 密码 | Windows DPAPI（CurrentUser） |
| `%LOCALAPPDATA%\RbxLite\profile.json` | 注册资料 + 启动器密码 | Windows DPAPI |
| `%LOCALAPPDATA%\RbxLite\social.json` | 聊天记录 | 明文（只在本地） |

DPAPI 加密的数据换机器或换 Windows 账户都解不开。

## 报告问题

发现安全问题时请走 GitHub 的私密漏洞报告（Security → Report a vulnerability），
不要在公开 issue 里贴密钥或账号信息。
