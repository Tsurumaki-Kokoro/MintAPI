# osu-web 图片素材

来源：https://github.com/ppy/osu-web

上游提交：`e8dc75555a6e6cc9e9790b895224cabeb60a8938`

提取日期：2026-10-01。共 469 个文件：134 SVG、252 PNG、82 JPG、1 ICO，合计 16,898,997 字节。
保留上游路径和原始字节，包含仓库内全部支持的图片；不包含外部 CDN 图片、字体文件或构建时生成的素材。

## 调用

启动 API 后访问 `/assets/osu-web/index.html`，可搜索路径、筛选格式和预览图片。点击路径打开原图。
`manifest.json` 列出每个文件的 Web URL、原始路径、大小、SHA-256 和固定提交的上游链接。

```html
<img src="/assets/osu-web/public/images/layout/osu-logo-white.svg" alt="osu!">
<img src="/assets/osu-web/public/images/layout/avatar-guest.png" alt="Guest">
```

项目渲染模板使用已有的 `base_url`：

```html
<img src="{{ base_url }}/assets/osu-web/public/images/layout/osu-logo-white.svg" alt="osu!">
```

本地文件位于 `MintAPI/wwwroot/assets/osu-web/`。现有项目配置会将 wwwroot 资源复制到构建及发布输出。

## 更新

在仓库根目录运行，参数为干净的 osu-web Git 检出目录：

```sh
python3 scripts/import-osu-web-assets.py /path/to/osu-web
```

脚本按同样的目录结构重新提取图片、更新清单和许可证，并删除上一版清单中已被上游移除的素材。
更新后同步修改本说明中的提交、日期及数量。

## 来源与许可

图片版权归原作者所有。随附 `LICENCE` 为上游 AGPL-3.0 许可证原文。
上游 README 的 Licence 章节说明代码、设计和美术素材的署名及同许可证开源要求；osu! 和 ppy 品牌使用不包含在该许可中。
参见 https://github.com/ppy/osu-web#licence 。
