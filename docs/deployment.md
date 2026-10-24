# 部署与升级

本文面向部署与维护服务的使用者。Release 提供 Linux、Windows 原生完整包和 Compose 包；部署时填写 osu! OAuth 应用的 Client ID / Secret。

## 环境

| 部署方式 | 目标环境 | 需准备 |
| --- | --- | --- |
| Linux 原生 | Debian 12/13、Ubuntu 22.04/24.04/26.04，x64 | sudo 权限；首次安装联网获取系统运行库 |
| Windows 原生 | Windows 11、Windows Server 2019+，x64 | 管理员权限；已有 Redis / 兼容缓存地址 |
| Compose | 可运行 Linux amd64 容器的 Docker Engine / Docker Desktop | Compose v2；可访问 Docker Hub |

原生包包含 .NET 运行时、Chromium、原生 PP 库和预览工具，无需另装 SDK、Rust、Git、Python 或 C/C++ 编译器。ARM64 和 Alpine 不在原生包支持范围。各版本的平台测试结果以 Release 说明为准。

## Debian / Ubuntu 原生安装

从 Release 下载 `mintapi-<版本>-linux-x64.tar.gz` 并验证 SHA256SUMS，解压到无空格的普通目录：

```sh
tar -xzf mintapi-<版本>-linux-x64.tar.gz
cd mintapi-<版本>-linux-x64
sudo bash install.sh
```

安装入口先安装 Chromium 所需共享库并执行离线包自检，再询问：

1. 是否代装 MariaDB（明确为 MySQL 兼容数据库）。选否转入数据库地址、端口、库名、用户名、密码和 TLS 验证输入；连接失败允许重新输入。使用已有服务时须先建立数据库，并给应用账号提供该库内的迁移权限。
2. 是否代装 Redis。选否转入缓存地址、端口、ACL 用户、密码和 TLS 输入。
3. osu! Client ID / Secret 与监听 URL。API Key 首次自动生成；再次安装可保留原值。

MariaDB/Redis 分别选择。代装检测到既有数据库/缓存软件包时拒绝覆盖，改用已有地址。代装只建立 `mintapi` 专用库与账号；缓存仅绑定本机并启用随机密码。成功代装后的连接凭证立即写入受限配置，以便后续问答失败时重试。

默认监听 `http://127.0.0.1:8080`，供本机插件或反向代理使用；需要局域网访问可填写具体地址或 `http://0.0.0.0:8080`。安装器不自动开放公网防火墙。HTTPS 在反向代理终止。

- 应用版本：`/opt/mintapi/releases/`，当前版本为 `/opt/mintapi/current`。
- 配置/API Key：`/var/lib/mintapi/appsettings.Production.json`，仅 root / mintapi 可读。
- 数据：同一目录的 `cache/`、`logs/`、`tokens/`，不会随应用升级删除。
- 服务：`systemctl status|restart|stop mintapi`；控制台日志 `journalctl -u mintapi`。

## Windows 原生安装

从 Release 下载 `mintapi-<版本>-win-x64.zip`，核验 SHA256SUMS，解压后以管理员身份运行 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

数据库可选代装独立的 MySQL Community 服务 `MintAPIMySQL`。Windows 缓存**不代装**，直接输入已有 Redis / 兼容服务的连接参数，包括 ACL 与 TLS。可使用另一台主机或已有容器提供的缓存。

MySQL 可选组件在发布构建时从官方固定版本 ZIP 获取。仅在操作者选择代装后初始化；遇到已有受管服务、数据目录或 3306 端口占用时拒绝重建。MySQL 的管理员随机密码保存在受限目录 `%ProgramData%\MintAPI-MySQL\root-password.txt`，应用仅使用 `mintapi` 专用账号。VC 运行库使用原厂安装程序，许可见随包 THIRD_PARTY_NOTICES.md。

- 应用版本：`%ProgramFiles%\MintAPI\releases\`。
- 配置/API Key：`%ProgramData%\MintAPI\appsettings.Production.json`。
- 服务：`Get-Service MintAPI`、`Restart-Service MintAPI`。
- 应用服务以 LocalService 身份运行；配置目录仅管理员、SYSTEM 与 LocalService 可访问。

安装器不更改机器全局环境变量；配置路径通过服务专用环境传入。配置失败不启动新服务；旧应用版本和用户数据保留。

## Docker Compose

安装 Docker Engine / Docker Desktop 和 Compose v2 后，从 Release 下载 Compose 包并解压：

```sh
cd compose
bash configure.sh
```

Windows 使用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\configure.ps1`。

首次填写 Docker Hub 镜像名、固定版本、osu! Client ID / Secret；脚本生成独立 API Key、数据库密码、root 密码和缓存密码，写入 `.env`，然后拉取预编译镜像并启动。已有 `.env` 时不重新生成凭据。也可手动复制 `.env.example` 为 `.env` 后填写。

Compose 包含 MintAPI、MySQL 8.4.8 与 Redis 7.4.2。数据库和缓存不映射主机端口；应用默认仅监听主机回环地址 8080。首次等待真实数据库查询和 Redis PING 成功后启动应用，再等待 `/health/ready`。数据保存在三个命名卷中。

```sh
docker compose ps
docker compose logs --tail=100 mintapi
docker compose stop
```

如需使用已有数据库/缓存，在本地修改 Compose，删除对应服务与 depends_on，改写应用的两个 ConnectionStrings 配置。不要用 `docker compose down -v` 升级；这会删除数据卷。不要将 `.env`、配置文件或日志上传至 Git。

## 检查服务状态

应用目录中可运行：

```sh
./MintAPI --self-test --output help-smoke.png
./MintAPI --check --config /path/to/appsettings.Production.json
./MintAPI --probe --url http://127.0.0.1:8080/health/ready
./MintAPI --wait-ready --config /path/to/appsettings.Production.json
```

Windows 使用 `MintAPI.exe`。`--self-test` 检查原生库、浏览器渲染和预览工具；`--check` 检查数据库及缓存连接，不执行迁移；`--probe` 检查已启动服务，`--wait-ready` 等待服务就绪。失败退出码非零。

`GET /health/live` 检查进程，`GET /health/ready` 检查依赖就绪状态。启动失败时可能连接拒绝；已启动但依赖不可用时返回 503。插件接入及帮助海报接口见 [服务信息与帮助](service-info.md)。

## 升级与恢复

1. 备份数据库、配置与需要保留的存储数据，核对版本说明中的配置/数据库变化。
2. 原生：解压新包重新运行安装入口，选择保留现有连接和 API Key。版本文件与数据目录分离，原配置留备份。
3. Compose：保留 `.env` 和卷，只修改固定版本，运行 `docker compose pull && docker compose up -d --wait`。
4. 首次启动新版本会自动执行数据库迁移，等待服务就绪后再恢复请求。
5. 检查 `/health/ready`、带鉴权的 `/api/info`，再执行真实 osu! 用户查询和图像请求。

原生保留旧版本文件，启动失败时可人工切回旧应用；**数据库迁移不自动反向执行**。应用回退前须确认 schema 兼容，必要时从备份恢复数据库。不要直接删除数据库目录来修复启动问题。
