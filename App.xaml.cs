using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RxNeL;

public partial class App : Application
{
    private Mutex? _mutex;
    private int _exitCode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _startTicks = Environment.TickCount64;

        Paths.EnsureInitialized();
        Logger.Initialize();

        Perf.Detect();
        Perf.ApplyAnimationBudget();

        var args = e.Args;

        if (TryRunCli(args))
        {
            Shutdown(_exitCode);
            return;
        }

        var uri = ExtractUri(args);

        _mutex = new Mutex(true, @"Local\RxNeL.SingleInstance.v1", out var isFirstInstance);

        if (!isFirstInstance && SingleInstance.Send(uri ?? SingleInstance.ShowCommand))
        {
            Shutdown();
            return;
        }

        SingleInstance.StartServer();

        ThemeManager.Initialize(ThemeManager.ParseMode(AppSettings.Current.ThemeMode));
        Runtime.Channel = AppSettings.Current.Channel;
        Runtime.InstalledVersion = Deployment.GetNewestInstalledVersion();

        Logger.Info($"[时序] 初始化完成: {Program_ElapsedMs()} ms");

        var window = new MainWindow(uri);
        MainWindow = window;

        Logger.Info($"[时序] 主窗口构建: {Program_ElapsedMs()} ms");

        window.Show();
        Logger.Info($"[时序] 主窗口显示: {Program_ElapsedMs()} ms");

        window.SelectPage(0);

        // 不再全量预热：页面本身就是 10~130ms 的懒构建，预热反而把启动拖长 1.4 秒
        window.SetLoadingStatus("就绪", 1);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.CompleteLoading();
            Logger.Info($"[启动] 界面可用: {Program_ElapsedMs()} ms");
        };
        timer.Start();
    }

    private static long _startTicks;

    private static long Program_ElapsedMs() => Environment.TickCount64 - _startTicks;

    // ─────────────────────────── 命令行 ───────────────────────────

    private bool TryRunCli(string[] args)
    {
        if (args.Any(a => a.Equals("--register", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = ProtocolRegistrar.RegisterPlayer();
            Logger.Info(ok ? "命令行注册协议成功" : "命令行注册协议失败");
            _exitCode = ok ? 0 : 1;
            return true;
        }

        if (args.Any(a => a.Equals("--unregister", StringComparison.OrdinalIgnoreCase)))
        {
            ProtocolRegistrar.UnregisterAll();
            return true;
        }

        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            _exitCode = SelfTest.Run();
            return true;
        }

        var holdIndex = Array.FindIndex(args, a => a.Equals("--hold-mutex", StringComparison.OrdinalIgnoreCase));

        if (holdIndex >= 0)
        {
            var name = holdIndex + 1 < args.Length ? args[holdIndex + 1] : "RxNeL_hold_mutex";
            var holdMs = 8000;

            if (holdIndex + 2 < args.Length && int.TryParse(args[holdIndex + 2], out var parsed))
                holdMs = Math.Clamp(parsed, 500, 120_000);

            using var held = new Mutex(false, name);
            Logger.Info($"占用互斥体 {name}，持续 {holdMs} ms");
            Thread.Sleep(holdMs);
            return true;
        }

        if (args.Any(a => a.Equals("--prepare", StringComparison.OrdinalIgnoreCase)))
        {
            var installed = Deployment.GetNewestInstalledVersion();

            if (installed is null)
            {
                Console.WriteLine("ERROR 没有已安装的客户端");
                _exitCode = 1;
                return true;
            }

            var timing = ClientPrep.Prepare(installed, AppSettings.Current);
            Console.WriteLine($"version={installed} fastflags={timing.FastFlagsMs}ms mods={timing.ModsMs}ms " +
                              $"font={timing.FontMs}ms gpu={timing.GpuMs}ms total={timing.TotalMs}ms");
            return true;
        }

        var fontIndex = Array.FindIndex(args, a => a.Equals("--font", StringComparison.OrdinalIgnoreCase));

        if (fontIndex >= 0)
        {
            var installed = Deployment.GetNewestInstalledVersion();

            if (installed is null)
            {
                Console.WriteLine("ERROR 没有已安装的客户端");
                _exitCode = 1;
                return true;
            }

            var target = fontIndex + 1 < args.Length ? args[fontIndex + 1] : string.Empty;
            var all = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(target) || target.Equals("restore", StringComparison.OrdinalIgnoreCase))
            {
                var restored = FontModManager.Restore(installed);
                Console.WriteLine($"version={installed} {restored.Message}");
                _exitCode = restored.Success ? 0 : 1;
                return true;
            }

            var result = FontModManager.ApplyExistingFont(installed, target, all);
            Console.WriteLine($"version={installed} {result.Message}");
            _exitCode = result.Success ? 0 : 1;
            return true;
        }

        var addIndex = Array.FindIndex(args, a => a.Equals("--addaccount", StringComparison.OrdinalIgnoreCase));

        if (addIndex >= 0 && addIndex + 1 < args.Length)
        {
            var user = args[addIndex + 1];
            var password = addIndex + 2 < args.Length ? args[addIndex + 2] : string.Empty;

            Accounts.AccountVault.Load();
            Accounts.AccountVault.AddOrUpdate(user, password, "本地添加");
            Console.WriteLine($"added {user}, hasPassword={!string.IsNullOrEmpty(password)}");
            return true;
        }

        var delIndex = Array.FindIndex(args, a => a.Equals("--delaccount", StringComparison.OrdinalIgnoreCase));

        if (delIndex >= 0 && delIndex + 1 < args.Length)
        {
            Accounts.AccountVault.Load();
            var target = Accounts.AccountVault.Find(args[delIndex + 1]);

            if (target is not null)
            {
                Accounts.AccountVault.Remove(target);
                Console.WriteLine($"removed {target.Username}");
            }

            return true;
        }

        if (args.Any(a => a.Equals("--listaccounts", StringComparison.OrdinalIgnoreCase)))
        {
            Accounts.AccountVault.Load();

            foreach (var record in Accounts.AccountVault.Accounts)
                Console.WriteLine($"{record.Username} | {record.Status} | password={(record.HasPassword ? "已加密" : "—")}");

            return true;
        }

        var relayIndex = Array.FindIndex(args, a => a.Equals("--relay", StringComparison.OrdinalIgnoreCase));

        if (relayIndex >= 0 && relayIndex + 1 < args.Length)
        {
            AppSettings.Current.SupabaseUrl = args[relayIndex + 1].TrimEnd('/');
            AppSettings.Current.RelayToken = relayIndex + 2 < args.Length ? args[relayIndex + 2] : "";
            AppSettings.Current.Save();

            Console.WriteLine($"relay = {AppSettings.Current.SupabaseUrl} (token set: {AppSettings.Current.RelayToken.Length > 0})");

            var check = System.Threading.Tasks.Task
                .Run(() => Backend.BackendSetup.VerifyAsync(AppSettings.Current.SupabaseUrl, AppSettings.Current.RelayToken))
                .GetAwaiter().GetResult();

            Console.WriteLine("check: " + check);
            return true;
        }

        var backendIndex = Array.FindIndex(args, a => a.Equals("--backend", StringComparison.OrdinalIgnoreCase));

        if (backendIndex >= 0 && backendIndex + 2 < args.Length)
        {
            var url = args[backendIndex + 1];
            var anon = args[backendIndex + 2];
            var pat = backendIndex + 3 < args.Length ? args[backendIndex + 3] : null;

            AppSettings.Current.SupabaseUrl = url.TrimEnd('/');
            AppSettings.Current.SupabaseAnonKey = anon;
            AppSettings.Current.Save();

            Console.WriteLine($"saved url = {AppSettings.Current.SupabaseUrl}");
            Console.WriteLine($"ref       = {Backend.BackendSetup.RefFromUrl(url) ?? "(解析失败)"}");

            if (pat is not null)
                Console.WriteLine("schema: " + System.Threading.Tasks.Task
                    .Run(() => Backend.BackendSetup.ApplySchemaAsync(url, pat))
                    .GetAwaiter().GetResult());

            Console.WriteLine("check: " + System.Threading.Tasks.Task
                .Run(() => Backend.BackendSetup.VerifyAsync(url, anon))
                .GetAwaiter().GetResult());

            return true;
        }

        if (args.Any(a => a.Equals("--update-mode", StringComparison.OrdinalIgnoreCase)))
        {
            var installed = UpdateChecker.IsInstalled(out var location);
            Console.WriteLine($"installed={installed} location={location ?? "(绿色版)"}");
            Console.WriteLine($"local version={UpdateChecker.LocalVersion}");
            return true;
        }

        if (args.Any(a => a.Equals("--netdiag", StringComparison.OrdinalIgnoreCase)))
        {
            var report = System.Threading.Tasks.Task.Run(() => Diagnostics.NetDiagAsync())
                .GetAwaiter().GetResult();

            Console.WriteLine(report);
            return true;
        }

        var openIndex = Array.FindIndex(args, a => a.Equals("--open", StringComparison.OrdinalIgnoreCase));

        if (openIndex >= 0 && openIndex + 1 < args.Length)
        {
            var output = System.Threading.Tasks.Task
                .Run(() => Backend.CloudSocial.OpenAsync(args[openIndex + 1]))
                .GetAwaiter().GetResult();

            Console.WriteLine(output);
            return true;
        }

        var awIndex = Array.FindIndex(args, a => a.Equals("--appwrite", StringComparison.OrdinalIgnoreCase));

        if (awIndex >= 0 && awIndex + 2 < args.Length)
        {
            AppSettings.Current.AppwriteEndpoint = args[awIndex + 1].TrimEnd('/');
            AppSettings.Current.AppwriteProjectId = args[awIndex + 2];
            AppSettings.Current.Save();

            Console.WriteLine($"appwrite = {AppSettings.Current.AppwriteEndpoint} (project {AppSettings.Current.AppwriteProjectId})");

            var health = System.Threading.Tasks.Task.Run(async () =>
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
                    AppSettings.Current.AppwriteEndpoint + "/health");

                request.Headers.TryAddWithoutValidation("X-Appwrite-Project", AppSettings.Current.AppwriteProjectId);

                using var response = await http.SendAsync(request);
                return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}".Trim();
            }).GetAwaiter().GetResult();

            Console.WriteLine("health: " + health);
            return true;
        }

        var smtpIndex = Array.FindIndex(args, a => a.Equals("--smtp", StringComparison.OrdinalIgnoreCase));

        if (smtpIndex >= 0 && smtpIndex + 7 < args.Length)
        {
            var url = args[smtpIndex + 1];
            var pat = args[smtpIndex + 2];
            var host = args[smtpIndex + 3];
            var port = int.Parse(args[smtpIndex + 4]);
            var user = args[smtpIndex + 5];
            var pass = args[smtpIndex + 6];
            var from = args[smtpIndex + 7];
            var fromName = smtpIndex + 8 < args.Length ? args[smtpIndex + 8] : "Rx | NeL";

            Console.WriteLine(System.Threading.Tasks.Task
                .Run(() => Backend.BackendSetup.ApplySmtpAsync(url, pat, host, port, user, pass, from, fromName))
                .GetAwaiter().GetResult());

            return true;
        }

        var verifyIndex = Array.FindIndex(args, a => a.Equals("--verify", StringComparison.OrdinalIgnoreCase));

        if (verifyIndex >= 0 && verifyIndex + 2 < args.Length)
        {
            var email = args[verifyIndex + 1];
            var input = args[verifyIndex + 2];

            var output = System.Threading.Tasks.Task.Run(async () =>
            {
                var client = Backend.SupabaseClient.Current;
                var isLink = input.Contains("token=", StringComparison.OrdinalIgnoreCase);

                if (!isLink)
                {
                    var session = await client.VerifyEmailOtpAsync(email, input.Trim());
                    return $"otp 验证成功 uid={session.UserId}";
                }

                var token = System.Text.RegularExpressions.Regex
                    .Match(input, @"token=([^&]+)").Groups[1].Value;

                token = Uri.UnescapeDataString(token);

                var linked = await client.VerifyEmailOtpAsync(email, token, "signup");
                return $"链接验证成功 uid={linked.UserId} confirmed={linked.EmailConfirmed}";
            }).GetAwaiter().GetResult();

            Console.WriteLine(output);
            return true;
        }

        var regIndex = Array.FindIndex(args, a => a.Equals("--regcheck", StringComparison.OrdinalIgnoreCase));

        if (regIndex >= 0 && regIndex + 2 < args.Length)
        {
            var email = args[regIndex + 1];
            var user = args[regIndex + 2];

            var emailCheck = Social.Profile.ValidateEmail(email);
            Console.WriteLine($"email: {emailCheck.Ok} {emailCheck.Message}");

            var lookup = System.Threading.Tasks.Task.Run(() => Accounts.RobloxAuth.LookupAsync(user))
                .GetAwaiter().GetResult();

            Console.WriteLine($"roblox: exists={lookup.Exists} name={lookup.LoginName} id={lookup.UserId}");
            Console.WriteLine($"machineRegistered={Social.Profile.MachineAlreadyRegistered()}");
            return true;
        }

        var peerIndex = Array.FindIndex(args, a => a.Equals("--peertest", StringComparison.OrdinalIgnoreCase));

        if (peerIndex >= 0)
        {
            var name = peerIndex + 1 < args.Length ? args[peerIndex + 1] : "peer";
            var seconds = peerIndex + 2 < args.Length && int.TryParse(args[peerIndex + 2], out var s2) ? s2 : 8;

            var presence = new Social.LanPresence { SelfName = name };
            var chat = new Social.ChatService { SelfId = presence.SelfId, SelfName = name };

            chat.MessageReceived += m => Console.WriteLine($"[{name}] recv from {m.FromName}: {m.Text}");

            chat.Start();
            presence.ChatPort = chat.Port;
            presence.Start();

            for (var i = 0; i < seconds; i++)
            {
                System.Threading.Thread.Sleep(1000);

                foreach (var peer in presence.Peers)
                    Console.WriteLine($"[{name}] peer {peer.Name} {peer.Address} {peer.PresenceText}");
            }

            if (peerIndex + 3 < args.Length)
            {
                var target = args[peerIndex + 3] == "auto"
                    ? presence.Peers.FirstOrDefault()?.Address ?? "127.0.0.1"
                    : args[peerIndex + 3];
                var text = peerIndex + 4 < args.Length ? args[peerIndex + 4] : "hello";

                var port = presence.Peers.FirstOrDefault(p => p.Address == target)?.ChatPort ?? 27101;
                var sent = chat.SendAsync(new[] { (target, port) }, new Social.ChatMessage
                {
                    ConversationId = "test",
                    FromId = chat.SelfId,
                    FromName = name,
                    Text = text
                }).GetAwaiter().GetResult();

                Console.WriteLine($"[{name}] sent to {target}: {sent}");
                System.Threading.Thread.Sleep(1500);
            }

            chat.Dispose();
            presence.Dispose();
            return true;
        }

        var checkIndex = Array.FindIndex(args, a => a.Equals("--checkuser", StringComparison.OrdinalIgnoreCase));

        if (checkIndex >= 0 && checkIndex + 1 < args.Length)
        {
            var username = args[checkIndex + 1];
            // 放到线程池执行，避免 UI 线程上 .Result 与 Dispatcher 同步上下文互锁
            var result = System.Threading.Tasks.Task
                .Run(() => Accounts.RobloxAuth.LookupAsync(username))
                .GetAwaiter().GetResult();

            Console.WriteLine($"reachable={result.Reachable} exists={result.Exists} id={result.UserId} " +
                              $"display={result.DisplayName} message={result.Message}");

            _exitCode = result.Reachable ? 0 : 2;
            return true;
        }

        if (args.Any(a => a.Equals("--vaulttest", StringComparison.OrdinalIgnoreCase)))
        {
            var secret = "pwd-" + Guid.NewGuid().ToString("N")[..10];
            var sealedText = Accounts.Dpapi.Protect(secret);
            var opened = Accounts.Dpapi.Unprotect(sealedText);

            Console.WriteLine($"dpapi ok = {opened == secret}");
            Console.WriteLine($"cipher length = {sealedText.Length}");

            Accounts.AccountVault.Load();
            var record = Accounts.AccountVault.AddOrUpdate("vault-selftest", secret, "本地自检");
            var back = Accounts.AccountVault.GetPassword(record);

            Console.WriteLine($"vault roundtrip = {back == secret}");
            Console.WriteLine($"vault file = {Accounts.AccountVault.FilePath}");
            Console.WriteLine($"file contains plaintext = {System.IO.File.ReadAllText(Accounts.AccountVault.FilePath).Contains(secret)}");

            Accounts.AccountVault.Remove(record);
            return true;
        }

        if (args.Any(a => a.Equals("--perftest", StringComparison.OrdinalIgnoreCase)))
        {
            ThemeManager.Initialize(ThemeManager.ParseMode(AppSettings.Current.ThemeMode));
            PerfTest.Run();

            _exitCode = 0;
            return true;
        }

        var uiTestIndex = Array.FindIndex(args, a => a.StartsWith("--uitest", StringComparison.OrdinalIgnoreCase));

        if (uiTestIndex >= 0)
        {
            var outDir = uiTestIndex + 1 < args.Length
                ? args[uiTestIndex + 1]
                : System.IO.Path.Combine(Paths.AppDir, "uitest");

            ThemeManager.Initialize(ThemeManager.ParseMode(AppSettings.Current.ThemeMode));
            UiTest.Run(outDir);

            _exitCode = 0;
            return true;
        }

        return false;
    }

    private static string? ExtractUri(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("-player", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--player", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-inject", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) return args[i + 1].Trim();
            }

            if (arg.StartsWith(LaunchHandler.ProtocolPrefix, StringComparison.OrdinalIgnoreCase))
                return arg.Trim();
        }

        return null;
    }
}
