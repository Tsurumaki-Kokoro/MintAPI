# MintAPI Python → .NET 重构详细计划

> 源项目：`/Users/yang/Code/MintAPI`（FastAPI + Pillow）
> 目标项目：`/Users/yang/Code/RiderProjects/MintAPI`（ASP.NET Core 9）
> 制定日期：2026-04-22

---

## 已完成组件

| 组件 | 状态 | 位置 |
|------|------|------|
| MintOsuApi C# 客户端库 | ✅ 完成 | `../mint-osuapi/src/MintOsuApi/` |
| RosuPP P/Invoke 绑定 | ✅ 完成 | `MintAPI/rosu_pp/RosuPP.cs` |
| librosu_pp_ffi.dylib | ✅ 已编译 | `/Users/yang/Code/Rust/rosu-pp-ffi/target/release/` |

---

## 技术栈

| 组件 | Python 原版 | .NET 版本 |
|------|------------|-----------|
| Web 框架 | FastAPI | ASP.NET Core 9 Controllers |
| ORM | tortoise-orm | EF Core 9 + Pomelo.EntityFrameworkCore.MySql |
| 缓存 | redis-py | StackExchange.Redis |
| 图片渲染 | Pillow + pyppeteer | Microsoft.Playwright + Scriban 模板引擎 |
| PP 计算 | rosu_pp_py | librosu_pp_ffi.dylib（FFI，已有 RosuPP.cs） |
| osu! API | ossapi | ../mint-osuapi/src/MintOsuApi（已迁移） |
| 限流 | slowapi | ASP.NET Core RateLimiter |
| 日志 | loguru | Serilog |
| HTTP 客户端 | httpx | HttpClient + IHttpClientFactory |
| 配置 | settings.py | appsettings.json + IOptions<T> |
| 鉴权 | APIKeyHeader | ApiKeyMiddleware |
| API 兼容性 | — | 基本兼容（路由一致，参数名允许小幅调整） |

---

## API 端点清单

| 路由 | 方法 | 功能 | 输出类型 |
|------|------|------|---------|
| `/beatmap/cover` | GET | beatmapset 封面图 | image/jpeg |
| `/beatmap/info` | GET | beatmap 信息卡片图 | image/jpeg |
| `/score/recent_play` | GET | 最近游玩记录图 | image/jpeg |
| `/score/best_play` | GET | 最佳游玩记录图 | image/jpeg |
| `/score/user_score` | GET | 指定谱面用户成绩图 | image/jpeg |
| `/user_info` | GET | 玩家信息图（含历史对比） | image/jpeg |
| `/user_info/update_background` | POST | 上传玩家自定义背景图 | application/json |
| `/user_info/extra/performance_control` | GET | PP 控制计算（二分查找最优 PP） | application/json |
| `/user_info/extra/performance_analyze` | GET | BP 分析图（ECharts） | image/jpeg |
| `/multiplayer/history` | GET | 多人房间对局历史图 | image/jpeg |
| `/multiplayer/rating` | GET | 多人房间评分图（3 种算法） | image/jpeg |
| `/user` | POST/GET | 用户注册与查询 | application/json |
| `/task/*` | POST | 定时任务手动触发 | application/json |

### 请求参数说明

**`/score/recent_play` & `/score/best_play`**：
- `platform`：平台标识（如 "qq"）
- `platform_uid`：平台用户 ID
- `game_mode`（可选）：游戏模式整数（0=osu, 1=taiko, 2=catch, 3=mania）
- `include_fails`（recent 专用，可选）：是否包含 fail 记录
- `best_index`（best 专用，默认 1）：第几个 BP
- `theme`（默认 "default"）：主题名

**`/user_info`**：
- `platform`、`platform_uid`、`game_mode`（可选）
- `user_name`（可选）：按用户名查找
- `compare_with`（可选）：与 N 天前数据对比
- `theme`（默认 "default"）

**`/multiplayer/rating`**：
- `algorithm`：`osuplus` | `bathbot` | `flashlight`
- `mp_id`：房间 ID
- `theme`（默认 "default"）

---

## 数据库模型

### UserModel（表名：`user`）
```
id            int PK
osu_uid       varchar(255)
game_mode     int (默认 0)
platform      varchar(255)
platform_uid  varchar(255)
unique: (platform, platform_uid)
```

### UserOsuInfoHistory（表名：`user_osu_info_history`）
```
id             int PK
osu_uid        varchar(255)
game_mode      int
country_rank   int? (null)
global_rank    int? (null)
pp             float? (null)
accuracy       float? (null)
play_count     int? (null)
play_time      int? (null)
total_hits     int? (null)
date           date (auto_now_add)
index: (id, osu_uid, date)
```

---

## 目标项目目录结构

```
MintAPI/
├── MIGRATION_PLAN.md                          ← 本文件
├── MintAPI/
│   ├── MintAPI.csproj                    # Phase 1：添加所有 NuGet 包
│   ├── Program.cs                             # Phase 1：完整服务注册
│   ├── appsettings.json                       # Phase 1：完整配置结构
│   ├── appsettings.Development.json           # Phase 1：开发环境配置
│   ├── rosu_pp/
│   │   └── RosuPP.cs                          # ✅ 已有
│   ├── Middleware/
│   │   └── ApiKeyMiddleware.cs                # Phase 1
│   ├── Models/
│   │   ├── Entities/
│   │   │   ├── UserModel.cs                   # Phase 1
│   │   │   └── UserOsuInfoHistory.cs          # Phase 1
│   │   └── Dtos/
│   │       ├── ScoreRequest.cs                # Phase 4
│   │       ├── UserInfoRequest.cs             # Phase 4
│   │       └── PpControlResult.cs             # Phase 4
│   ├── Data/
│   │   └── AppDbContext.cs                    # Phase 1
│   ├── Services/
│   │   ├── OsuApiService.cs                   # Phase 2：封装 MintOsuApi 调用
│   │   ├── PpCalculatorService.cs             # Phase 2：封装 RosuPP FFI
│   │   ├── BeatmapFileService.cs              # Phase 2：.osu 文件下载/缓存
│   │   ├── ImageCacheService.cs               # Phase 2：头像/背景/徽章缓存
│   │   ├── PlaywrightRenderer.cs              # Phase 2：HTML→图片
│   │   └── RedisCacheService.cs               # Phase 2：Redis 封装
│   ├── Rendering/
│   │   ├── ThemeRenderer.cs                   # Phase 3：主题加载器基类
│   │   ├── Score/
│   │   │   ├── ScoreThemeData.cs              # Phase 3：模板数据模型
│   │   │   └── templates/default/
│   │   │       ├── index.html                 # Phase 3：Scriban 模板
│   │   │       └── style.css
│   │   ├── UserInfo/
│   │   │   ├── UserInfoThemeData.cs
│   │   │   └── templates/default/
│   │   │       ├── index.html
│   │   │       └── style.css
│   │   ├── Beatmap/
│   │   │   └── templates/default/...
│   │   ├── Multiplayer/
│   │   │   └── templates/default/...
│   │   └── BpAnalyze/
│   │       └── templates/default/
│   │           ├── bpa_chart.html             # Phase 3：从 Python 迁移复用
│   │           └── mod_chart.html
│   ├── Controllers/
│   │   ├── BeatmapController.cs               # Phase 4
│   │   ├── ScoreController.cs                 # Phase 4
│   │   ├── UserInfoController.cs              # Phase 4
│   │   ├── MultiplayerController.cs           # Phase 4
│   │   ├── UserController.cs                  # Phase 4
│   │   └── TaskController.cs                  # Phase 4
│   └── wwwroot/
│       ├── fonts/                             # Phase 3：从 Python draw/fonts/ 复制
│       ├── flags/                             # Phase 3：从 Python draw/flags/ 复制
│       └── assets/                            # Phase 3：从 Python themes/*/assets/ 整合
├── ../mint-osuapi/src/MintOsuApi/                                # ✅ 已有
└── ../mint-osuapi/tests/MintOsuApi.Tests/                        # ✅ 已有
```

---

## Phase 1：基础设施

### 1.1 NuGet 包（MintAPI.csproj）

```xml
<!-- ORM -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="9.0.0" />

<!-- Redis -->
<PackageReference Include="StackExchange.Redis" Version="2.8.16" />

<!-- Playwright -->
<PackageReference Include="Microsoft.Playwright" Version="1.51.0" />

<!-- 模板引擎 -->
<PackageReference Include="Scriban" Version="5.12.0" />

<!-- 日志 -->
<PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />

<!-- 限流 -->
<!-- 内置于 ASP.NET Core 9，无需额外包 -->

<!-- HTTP -->
<!-- 内置 HttpClient，无需额外包 -->

<!-- OpenAPI -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
<PackageReference Include="Scalar.AspNetCore" Version="2.x" />

<!-- MintOsuApi 项目引用 -->
<ProjectReference Include="..\..\mint-osuapi\src\MintOsuApi\MintOsuApi.csproj" />
```

### 1.2 appsettings.json 结构

```json
{
  "ApiKey": "your_api_key",
  "OsuApi": {
    "ClientId": 0,
    "ClientSecret": ""
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=mintapi;User=root;Password=;",
    "Redis": "localhost:6379,db=1"
  },
  "CacheDir": "cache",
  "RateLimiting": {
    "DefaultLimitPerMinute": 20
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### 1.3 dylib 配置

```xml
<!-- MintAPI.csproj 中 -->
<ItemGroup>
  <None Include="/Users/yang/Code/Rust/rosu-pp-ffi/target/release/librosu_pp_ffi.dylib"
        CopyToOutputDirectory="PreserveNewest"
        Link="librosu_pp_ffi.dylib" />
</ItemGroup>
```

---

## Phase 2：核心服务

### 2.1 PpCalculatorService

对应 Python `app/osu_utils/pp.py` 中的 `PPCalculator` 类：

```csharp
public interface IPpCalculatorService
{
    // 对应 PPCalculator.pp_info()
    PerformanceResult CalculatePp(ScoreData score, string osuFilePath);

    // 对应 PPCalculator.if_pp_ss_pp_info()
    (double IfPp, double SsPp) CalculateIfFcAndSsPp(ScoreData score, string osuFilePath);

    // 对应 get_ss_pp_info()
    PerformanceResult GetSsPpInfo(string osuFilePath, int rulesetId, uint mods);

    // 对应 find_optimal_new_pp()
    (double OptimalPp, int Position) FindOptimalNewPp(List<double> currentPpList, double desiredIncrease);
}
```

**Mods 整数转换**（Python 使用字符串列表，RosuPP FFI 需要 uint）：
- 参考 osu! API 的 mod bitfield 标准
- NM=0, EZ=2, NF=1, HT=256, HR=16, SD=32, PF=16384, HD=8, FL=1024, RX=128, AP=8192, SO=4096, DT=64, NC=512

### 2.2 BeatmapFileService

对应 Python `app/osu_utils/beatmap.py` 中的 `get_osu_file_path`、`get_map_bg`：

```csharp
public interface IBeatmapFileService
{
    Task<string> GetOsuFilePathAsync(int beatmapsetId, int beatmapId);
    Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName);
    string? GetBgFilename(string osuFilePath);
}
```

缓存目录结构（与 Python 保持一致）：
```
cache/
├── beatmap/osu_file/{setId}/{mapId}.osu
├── beatmap/osu_file/{setId}/{bgName}.jpg
└── user/{osuUid}/info.png
```

### 2.3 PlaywrightRenderer

```csharp
public interface IPlaywrightRenderer
{
    Task<byte[]> RenderHtmlAsync(string html, int width, int height);
}

public class PlaywrightRenderer : IPlaywrightRenderer, IAsyncDisposable
{
    // 单例：一个 IPlaywright + 一个 IBrowser 实例
    // 每次渲染创建新 IPage，截图后关闭
    // 使用 SemaphoreSlim 限制并发
}
```

### 2.4 ImageCacheService

对应 Python `app/osu_utils/user.py` 中的 `get_user_avatar`、`get_user_badge`、`get_info_bg`：

```csharp
public interface IImageCacheService
{
    Task<byte[]> GetUserAvatarAsync(int userId, string avatarUrl);
    Task<byte[]?> GetUserBadgeAsync(int userId, string badgeUrl, string description);
    Task<byte[]?> GetInfoBgAsync(int userId);
}
```

---

## Phase 3：HTML 模板（渲染层）

### 3.1 渲染流程

```csharp
// ThemeRenderer.cs 基类
public abstract class ThemeRenderer<TData>
{
    protected readonly IPlaywrightRenderer _renderer;

    public async Task<byte[]> RenderAsync(TData data, string theme = "default")
    {
        var templatePath = GetTemplatePath(theme);
        var template = Template.Parse(File.ReadAllText(templatePath));
        var html = template.Render(data);
        return await _renderer.RenderHtmlAsync(html, Width, Height);
    }

    protected abstract string GetTemplatePath(string theme);
    protected abstract int Width { get; }
    protected abstract int Height { get; }
}
```

### 3.2 各图片类型尺寸

| 图片类型 | 尺寸 | 对应 Python 文件 |
|---------|------|----------------|
| score_image | 1500×720 | `draw/score.py` |
| user_info_image | 1000×1350 | `themes/user_info_image/default/theme.py` |
| beatmap_image | (待确认) | `draw/beatmap.py` |
| match_history_image | (待确认) | `draw/multiplayer.py` |
| rating_image | (待确认) | `draw/multiplayer.py` |
| bp_analyze_image | 950×950 | `themes/bp_analyze_image/default/theme.py` |

### 3.3 资源迁移

从 Python 项目复制静态资源到 `wwwroot/`：
- `app/draw/fonts/` → `wwwroot/fonts/`
- `app/draw/flags/` → `wwwroot/flags/`
- `app/themes/score_image/default/assets/` → `wwwroot/assets/score/`
- `app/themes/user_info_image/default/assets/` → `wwwroot/assets/user_info/`
- `app/themes/beatmap_image/default/assets/` → `wwwroot/assets/beatmap/`
- `app/themes/match_history_image/default/assets/` → `wwwroot/assets/multiplayer/`
- `app/themes/rating_image/default/assets/` → `wwwroot/assets/rating/`

### 3.4 BP 分析模板（可复用）

Python 现有 `app/themes/bp_analyze_image/default/templates/bpa_chart.html` 和 `mod_chart.html`
已经是 Jinja2 模板 + ECharts，变量语法从 `{{ var }}` 改为 Scriban 的 `{{ var }}` 基本兼容。

---

## Phase 4：API Controllers

### 4.1 ScoreController

```csharp
[ApiController]
[Route("score")]
public class ScoreController(IScoreService scoreService) : ControllerBase
{
    [HttpGet("recent_play")]
    public Task<IActionResult> GetRecentPlay(
        string platform, string platform_uid,
        int? game_mode, bool include_fails = false,
        string theme = "default");

    [HttpGet("best_play")]
    public Task<IActionResult> GetBestPlay(
        string platform, string platform_uid,
        int? game_mode, int best_index = 1,
        string theme = "default");

    [HttpGet("user_score")]
    public Task<IActionResult> GetUserScore(
        string platform, string platform_uid,
        int beatmap_id, string? mods,
        string theme = "default");
}
```

### 4.2 UserInfoController

```csharp
[ApiController]
[Route("user_info")]
public class UserInfoController : ControllerBase
{
    [HttpGet("")]
    public Task<IActionResult> GetUserInfo(
        string platform, string platform_uid,
        int? game_mode, string? user_name,
        int? compare_with, string theme = "default");

    [HttpPost("update_background")]
    public Task<IActionResult> UpdateBackground(
        string platform, string platform_uid,
        IFormFile background_file);

    [HttpGet("extra/performance_control")]
    public Task<IActionResult> GetPerformanceControl(
        string platform, string platform_uid, double pp);

    [HttpGet("extra/performance_analyze")]
    public Task<IActionResult> GetPerformanceAnalyze(
        string platform, string platform_uid,
        string theme = "default");
}
```

---

## Phase 5：部署配置

### Dockerfile 更新

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
# 安装 Playwright 浏览器依赖
RUN apt-get update && apt-get install -y \
    libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 \
    libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 \
    libxrandr2 libgbm1 libasound2
WORKDIR /app
COPY --from=build /app/publish .
RUN dotnet tool install --global Microsoft.Playwright.CLI
RUN playwright install chromium
EXPOSE 8080
ENTRYPOINT ["dotnet", "MintAPI.dll"]
```

### 环境变量映射

| Python 变量 | .NET appsettings key |
|------------|---------------------|
| `API_KEY` | `ApiKey` |
| `CLIENT_ID` | `OsuApi:ClientId` |
| `CLIENT_SECRET` | `OsuApi:ClientSecret` |
| `DB_HOST/PORT/USER/PASSWORD` | `ConnectionStrings:DefaultConnection` |
| `REDIS_HOST/PORT/DB` | `ConnectionStrings:Redis` |

---

## 关键实现注意事项

1. **GIF 头像**：`<img src="data:image/gif;base64,...">` 注入 base64 GIF，Playwright 截图得到静态帧
2. **字体**：CSS `@font-face` 加载 `wwwroot/fonts/*.ttf`，模板中引用相对路径
3. **dylib 线程安全**：Rust `rosu-pp` 是线程安全的，可以多线程并发调用
4. **EF Core 迁移命令**：
   ```bash
   dotnet ef migrations add InitialCreate --project MintAPI
   dotnet ef database update --project MintAPI
   ```
5. **Playwright 初始化**：使用 `IHostedService` 在应用启动时预热浏览器
6. **PP 计算 Mods**：Python 使用字符串数组 `["HD", "DT"]`，RosuPP FFI 接受 `uint` bitfield，需实现转换
7. **Base64 图片传递**：资源图片（头像、背景、图标）通过 base64 编码内联进 HTML，避免 Playwright 文件路径问题

---

## 验证检查清单

- [ ] Phase 1：`dotnet build` 成功，无警告
- [ ] Phase 1：EF Core 迁移成功，数据库表创建正确
- [ ] Phase 2：PP 计算结果与 Python 版对比误差 < 0.1pp
- [ ] Phase 2：Playwright 可以成功截图一个简单 HTML
- [ ] Phase 3：score 图片视觉效果与 Python 版对比
- [ ] Phase 4：所有端点返回正确状态码和内容类型
- [ ] Phase 5：Docker 构建成功，`playwright install` 成功
