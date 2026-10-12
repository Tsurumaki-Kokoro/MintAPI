---
name: mintapi-rendering
description: 设计、调整或验证 MintAPI 的 HTML/SVG 图片模板和 PNG 渲染输出，包括成绩、玩家信息、谱面、列表及分析报告。用于排版、横竖版选择、阅读尺寸和视觉验收；纯后端业务或 JSON 接口变更无需使用。
---

# MintAPI 渲染

设计或调整图片前，阅读 [设计规范 v1](references/design-spec.md)，并遵循仓库 AGENTS.md 与用户当前要求。规范描述目标方向，不代表现有模板已经完成改造；仅修改本次请求涉及的输出。

## 工作方式

- 从项目根目录定位 `MintAPI/Rendering/` 中的主题和模板、`MintAPI/Services/` 中相关渲染器，以及 `tests/MintAPI.Tests/Integration/` 中对应的渲染测试。资源位于 `MintAPI/wwwroot/`。检查调用方、数据准备、实际导出尺寸及字段含义后确定布局。
- 列表优先竖版，详情与分析报告优先横版，avatar card 保持方形。按主体内容选择结构，保留现有字段、四模式差异和未知值。提高空间利用率时先调整分组、比例和分页，再调整字号。
- 用户仅要求规范或设计建议时，交付建议；要求实现时，将选定方向落实到对应模板和主题模型。不要从规范推导出全站改造授权。
- `frontend-design` 可用于视觉方向、排版和自检；`webapp-testing` 可辅助浏览器检查。两者可用时按任务需要使用，项目规范约束具体输出；无需把通用 Skill 的 Python 测试示例移植进已有 C# 流程。
- 实现后复用 `IRenderService` / Chromium PNG 渲染流程，运行相关测试，以同一数据生成前后对照，并打开检查图片。既检查原图，也检查接近聊天显示宽度的效果；覆盖任务相关的长文本、大数值、缺失字段、条目数及模式变化。
- 通过共享 `RenderAttribution` 保留适用主题的署名和北京时间绘制日期，并检查其可见性与内容重叠。`/user_info/avatar_card` 必须直接渲染，不调用 `RenderAttribution`，不添加任何水印、署名或日期。
- 交付时说明修改范围、真实图片路径和已完成的验证。仅有 HTML 断言或构建结果时，不宣称视觉验收完成。
