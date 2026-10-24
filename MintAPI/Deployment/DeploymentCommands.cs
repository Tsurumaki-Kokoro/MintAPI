using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Rendering.HelpTheme;
using MintAPI.Services;
using MySqlConnector;
using StackExchange.Redis;

namespace MintAPI.Deployment;

/// <summary>随预编译应用交付的安装问答和诊断入口，不启动 Web 服务或迁移数据库。</summary>
public static class DeploymentCommands
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Contains("--probe"))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var response = await http.GetAsync(Option(args, "--url") ?? "http://127.0.0.1:8080/health/ready");
                return response.IsSuccessStatusCode ? 0 : 1;
            }
            if (args.Contains("--self-test")) return await SelfTestAsync(Option(args, "--output"));
            var path = Path.GetFullPath(Option(args, "--config") ?? Environment.GetEnvironmentVariable("MINTAPI_CONFIG_FILE")
                ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Production.json"));
            if (args.Contains("--wait-ready"))
            {
                var config = new ConfigurationBuilder().AddJsonFile(path).Build();
                var address = config["Urls"] ?? "http://127.0.0.1:8080";
                var uri = new UriBuilder(address) { Path = "/health/ready" };
                if (uri.Host is "0.0.0.0" or "::" or "[::]") uri.Host = "127.0.0.1";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    try { using var response = await http.GetAsync(uri.Uri); if (response.IsSuccessStatusCode) { Console.WriteLine($"服务已就绪：{uri.Uri.GetLeftPart(UriPartial.Authority)}"); return 0; } }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
                throw new TimeoutException();
            }
            if (args.Contains("--setup")) await SetupAsync(path);
            else
            {
                var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json")
                    .AddJsonFile(path, optional: false).AddEnvironmentVariables().Build();
                await CheckConnectionsAsync(config.GetConnectionString("DefaultConnection")!, config.GetConnectionString("Redis")!);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("已取消；已有配置与依赖服务保留。");
            return 130;
        }
        catch (Exception ex)
        {
            // Driver exception messages may contain hosts or credentials. Do not echo them.
            Console.Error.WriteLine($"操作未完成（{ex.GetType().Name}）。配置与数据保留；检查服务状态、连接参数及部署文档后重试。");
            return 1;
        }
    }

    public static string? Option(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static string BuildMySqlConnection(string host, uint port, string database, string user, string password, bool tls)
        => new MySqlConnectionStringBuilder { Server = host, Port = port, Database = database, UserID = user,
            Password = password, SslMode = tls ? MySqlSslMode.VerifyFull : MySqlSslMode.Preferred,
            ConnectionTimeout = 10, DefaultCommandTimeout = 15 }.ConnectionString;

    public static string BuildRedisConnection(string host, int port, string user, string password, bool tls)
    {
        if (host.Contains(',') || user.Contains(',') || password.Contains(',') ||
            host.Any(char.IsControl) || user.Any(char.IsControl) || password.Any(char.IsControl))
            throw new ArgumentException("Redis connection-string credentials cannot contain commas or control characters");
        var options = new ConfigurationOptions { User = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = string.IsNullOrEmpty(password) ? null : password, Ssl = tls, AbortOnConnectFail = true,
            ConnectTimeout = 10000, AsyncTimeout = 10000, DefaultDatabase = 1 };
        options.EndPoints.Add(host, port);
        return options.ToString(includePassword: true);
    }

    private static async Task SetupAsync(string path)
    {
        Console.WriteLine("MintAPI 原生安装配置：数据库与缓存分别选择；任何已有实例都不会被覆盖。");
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var old = File.Exists(path) ? JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject() : new JsonObject();
        string mysql, redis;
        var existingDb = old["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>();
        if (existingDb != null && Yes("保留现有数据库连接？", true)) mysql = existingDb;
        else if (Yes(OperatingSystem.IsWindows() ? "代装独立 MySQL 服务？" : "代装 MariaDB（MySQL 兼容数据库）？", false))
        {
            var password = RandomSecret();
            await ProvisionAsync("database", password);
            mysql = BuildMySqlConnection("127.0.0.1", 3306, "mintapi", "mintapi", password, false);
            old["ConnectionStrings"] ??= new JsonObject();
            old["ConnectionStrings"]!["DefaultConnection"] = mysql;
            await WriteConfigurationAsync(path, old);
        }
        else mysql = await ReadMySqlAsync();
        var existingCache = old["ConnectionStrings"]?["Redis"]?.GetValue<string>();
        if (existingCache != null && Yes("保留现有缓存连接？", true)) redis = existingCache;
        else if (OperatingSystem.IsLinux() && Yes("代装本机 Redis 缓存服务？", false))
        {
            var password = RandomSecret();
            await ProvisionAsync("cache", password);
            redis = BuildRedisConnection("127.0.0.1", 6379, "", password, false);
            old["ConnectionStrings"] ??= new JsonObject();
            old["ConnectionStrings"]!["Redis"] = redis;
            await WriteConfigurationAsync(path, old);
        }
        else
        {
            if (OperatingSystem.IsWindows()) Console.WriteLine("Windows 使用已有 Redis / Redis 兼容服务，请输入连接信息。");
            redis = await ReadRedisAsync();
        }
        await CheckConnectionsAsync(mysql, redis);
        var id = Read("osu! Client ID", old["OsuApi"]?["ClientId"]?.ToString());
        if (!int.TryParse(id, out var clientId) || clientId <= 0) throw new ArgumentException("Client ID");
        var clientSecret = Secret("osu! Client Secret（留空保留已有值）");
        if (clientSecret.Length == 0) clientSecret = old["OsuApi"]?["ClientSecret"]?.GetValue<string>() ?? "";
        if (clientSecret.Length == 0) throw new ArgumentException("Client Secret");
        var apiKey = old["ApiKey"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(apiKey) || !Yes("保留现有 API Key？", true)) apiKey = RandomSecret();
        var address = Read("监听地址（回环地址供本机或反向代理使用）", old["Urls"]?.GetValue<string>() ?? "http://127.0.0.1:8080");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.AbsolutePath != "/")
            throw new ArgumentException("监听地址需要 HTTP，HTTPS 使用反向代理");
        var data = Path.GetDirectoryName(path)!;
        old["ApiKey"] = apiKey;
        old["Urls"] = address;
        old["OsuApi"] ??= new JsonObject();
        old["OsuApi"]!["ClientId"] = clientId;
        old["OsuApi"]!["ClientSecret"] = clientSecret;
        old["ConnectionStrings"] ??= new JsonObject();
        old["ConnectionStrings"]!["DefaultConnection"] = mysql;
        old["ConnectionStrings"]!["Redis"] = redis;
        old["CacheDir"] = Path.Combine(data, "cache");
        old["Storage"] ??= new JsonObject();
        old["Storage"]!["LogDirectory"] = Path.Combine(data, "logs");
        old["Storage"]!["TokenCacheDirectory"] = Path.Combine(data, "tokens");
        Console.WriteLine($"配置将写入：{path}\n监听：{address}\n数据库与缓存：已验证连接\nAPI Key、密码及 osu! 凭证：隐藏");
        if (!Yes("保存配置？", true)) throw new OperationCanceledException();
        await WriteConfigurationAsync(path, old);
        Console.WriteLine("配置已保存。API Key 可在此受限配置文件中查看。接下来由安装脚本注册并启动服务。");
    }

    private static async Task WriteConfigurationAsync(string path, JsonObject configuration)
    {
        var data = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(data);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (File.Exists(path)) File.Copy(path, path + ".backup-" + Guid.NewGuid().ToString("N"), false);
        var pending = path + ".pending";
        await File.WriteAllTextAsync(pending, configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pending, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(pending, path, true);
    }

    private static async Task<string> ReadMySqlAsync()
    {
        while (true)
        {
            var host = Read("数据库地址", "127.0.0.1");
            var port = Read("数据库端口", "3306");
            var database = Read("数据库名称", "mintapi");
            var user = Read("数据库用户名", "mintapi");
            var password = Secret("数据库密码");
            var tls = Yes("验证数据库 TLS 证书？", false);
            try
            {
                var connection = BuildMySqlConnection(host, (uint)Port(port), database, user, password, tls);
                await CheckMySqlAsync(connection);
                return connection;
            }
            catch (Exception) { Console.WriteLine("数据库连接失败，请检查地址、库名、账号及 TLS 设置后重新输入。"); }
        }
    }

    private static async Task<string> ReadRedisAsync()
    {
        while (true)
        {
            var host = Read("缓存地址", "127.0.0.1");
            var port = Read("缓存端口", "6379");
            var user = Read("缓存 ACL 用户（可留空）", "");
            var password = Secret("缓存密码（可留空；不支持逗号或控制字符）");
            var tls = Yes("启用缓存 TLS？", false);
            try
            {
                var connection = BuildRedisConnection(host, Port(port), user, password, tls);
                await CheckRedisAsync(connection);
                return connection;
            }
            catch (Exception) { Console.WriteLine("缓存连接失败，请检查地址、认证及 TLS 设置后重新输入。"); }
        }
    }

    private static async Task CheckConnectionsAsync(string mysql, string redis)
    {
        await CheckMySqlAsync(mysql);
        await CheckRedisAsync(redis);
        Console.WriteLine("数据库与缓存连接检查通过。");
    }

    private static async Task CheckMySqlAsync(string connection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync(deadline.Token);
        await using var command = new MySqlCommand("SELECT 1", db);
        await command.ExecuteScalarAsync(deadline.Token);
    }

    private static async Task CheckRedisAsync(string connection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = await ConnectionMultiplexer.ConnectAsync(connection).WaitAsync(deadline.Token);
        await client.GetDatabase().PingAsync().WaitAsync(deadline.Token);
    }

    private static async Task ProvisionAsync(string component, string password)
    {
        Console.WriteLine("代装会安装系统服务并初始化 MintAPI 专用账号；检测到已有实例会中止，请改选已有地址。");
        if (!Yes("开始代装？", false)) throw new OperationCanceledException();
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var script = Path.Combine(AppContext.BaseDirectory, "deployment", OperatingSystem.IsWindows() ? "provision-windows.ps1" : "provision-debian.sh");
        if (OperatingSystem.IsWindows())
            foreach (var item in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Component", component }) start.ArgumentList.Add(item);
        else { start.ArgumentList.Add(script); start.ArgumentList.Add(component); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(password);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException("Dependency provisioning failed; see deployment documentation");
        Console.WriteLine("依赖服务代装完成。");
    }

    private static async Task<int> SelfTestAsync(string? output)
    {
        var browsers = Path.Combine(AppContext.BaseDirectory, "browsers");
        if (Directory.Exists(browsers)) Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsers);
        var native = OperatingSystem.IsWindows() ? "rosu_pp_ffi.dll" : OperatingSystem.IsMacOS() ? "librosu_pp_ffi.dylib" : "librosu_pp_ffi.so";
        var handle = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, native));
        NativeLibrary.Free(handle);
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var png = await new HelpTheme(new PlaywrightRenderer(browser)).RenderAsync();
        if (!png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })) throw new InvalidDataException();
        if (output != null) await File.WriteAllBytesAsync(output, png);
        var preview = Path.Combine(AppContext.BaseDirectory, "tools", "osu-preview", "osu-beatmap-preview-cli" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var result = await new Services.Preview.PreviewCliRunner().RunAsync(preview, ["--version"], CancellationToken.None);
        if (result.ExitCode != 0) throw new InvalidOperationException();
        Console.WriteLine($"自检通过：原生 PP 库、PNG 渲染、Chromium {browser.Browser.Version}、预览工具。");
        return 0;
    }

    private static string Read(string label, string? fallback = null)
    {
        Console.Write($"{label}{(fallback != null ? $" [{fallback}]" : "")}：");
        var value = Console.ReadLine() ?? throw new OperationCanceledException();
        return value.Trim().Length == 0 && fallback != null ? fallback : value.Trim();
    }

    private static string Secret(string label)
    {
        Console.Write(label + "：");
        if (Console.IsInputRedirected) return Console.ReadLine() ?? throw new OperationCanceledException();
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
    }

    private static bool Yes(string label, bool fallback) => Read(label + " y/n", fallback ? "y" : "n").Equals("y", StringComparison.OrdinalIgnoreCase);
    private static int Port(string value) => int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : throw new ArgumentException("Invalid port");
    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
