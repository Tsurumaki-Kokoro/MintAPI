# 发布维护

本文供发布维护者使用。服务安装与升级见 [部署文档](deployment.md)。

## 配置 Actions

仓库 Settings → Secrets and variables → Actions：

| 类型 | 名称 | 值 |
| --- | --- | --- |
| Variable | `DOCKERHUB_USERNAME` | Docker Hub 用户名 |
| Secret | `DOCKERHUB_TOKEN` | 可读写镜像的访问令牌 |

Docker Hub 使用 `<用户名>/mintapi` 仓库。GitHub Release 使用自动提供的 `GITHUB_TOKEN`，无需手动添加。构建不需要生产 osu! 凭证、数据库密码或 SSH 密钥。

## 触发方式

| 触发方式 | 检查内容 | 发布行为 |
| --- | --- | --- |
| 推送 `main` 或提交 PR | 部署脚本、Bash、Compose 配置 | 不编译、不发布 |
| 手动运行 Release packages | 双平台编译、测试、打包和容器冒烟 | 不发布，临时产物随 runner 清理 |
| 推送 `v<语义版本>` 标签 | 完整构建与验证 | 发布 Release 和 Docker Hub 镜像 |

先手动运行工作流验证，再创建版本标签，例如：

```sh
git tag v0.1.0
git push origin v0.1.0
```

预发布使用 `v0.1.0-preview.1`。仅稳定版本更新 Docker Hub 的 `latest` 标签。

流程不上传 Actions artifact 或 Docker 构建记录。标签构建将原生包直接附加到草稿 Release，所有验证通过后公开。失败的草稿保留，可重试同一标签；已公开 Release 不被覆盖。

## 构建依赖

`deploy/dependencies.json` 固定 MintOsuApi、预览引擎的源码提交和 Windows MySQL 下载版本。相邻 `mint-osuapi` 仓库须检出到指定提交且工作区干净；私有客户端仓库需要另配跨仓库读取令牌。

升级 Playwright 时同时重新打包配套 Chromium，并检查实际 PNG。Windows MySQL 可选组件使用官方归档，包内保留许可证、声明和下载哈希。

## 本地构建发布包

构建机需要 SDK、Rust、Python 与 C/C++ 工具链。Linux 原生包使用 Debian 12 基线构建：

```sh
docker build -f deploy/build-linux.Dockerfile -t mintapi-linux-builder .
docker run --rm -v "$PWD:/work/mintapi" -v "$PWD/../mint-osuapi:/work/mint-osuapi" \
  -w /work/mintapi mintapi-linux-builder \
  python3 scripts/package-release.py --rid linux-x64 --version 0.1.0
```

Windows x64 构建机：

```powershell
python scripts/package-release.py --rid win-x64 --version 0.1.0
```

输出在 `artifacts/`，包括平台安装包、SHA-256 文件和渲染自检图片；包内 `manifest.sha256.json` 列出文件校验值。脚本输出压缩与展开字节数。

## 发布前检查

- 双平台测试、发布包自检和四模式 PNG/GIF/MP4 测试通过。
- 在干净目标系统完成安装、重启、升级与已有配置保留检查。
- 使用有效 osu! 凭证完成用户查询和图片请求。
- 更新版本说明，列出配置变化、迁移要求和测试平台。
- 核对第三方字体、图标、插画的分发许可，保留随包声明。

Release 附 Linux/Windows 原生包、Compose 包和 `SHA256SUMS`。数据库升级前备份；回退应用前确认数据库兼容性，不自动逆向执行迁移。
