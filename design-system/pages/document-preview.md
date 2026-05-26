# Document Preview 页面设计覆盖

- 页面路由：`/document-preview`、`/document-preview/:id`
- 页面类型：`Reading And Preview Workspace`
- 源文件：
  - `src/LightRAGNet.React/src/features/document-preview/DocumentPreviewPage.tsx`
  - `src/LightRAGNet.React/src/features/document-preview/document-preview.css`

## 页面角色

Document Preview 是完整页面的阅读工作区。它应比文档列表更安静：控件更少，阅读表面更大，内容可读性更强，元数据帮助用户确认当前正在查看的文档。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `StatusPill`
- `MarkdownRenderer`
- `EmptyState`
- `ErrorState`

## 允许偏离

- 阅读内容可以有页面专用的排版间距、行高、表格样式和代码块尺寸。
- 内容面板可以设置最大宽度来支持长文阅读。
- Markdown 标题可以比普通页面文本使用更强对比度。

## 规则

- 预览内容只能来自安全的 preview API。
- 本页面不能推断本地文件路径或浏览器 URL。
- 通用背景、边框和文本值使用共享 token。页面里的字面颜色若不是严格的内容渲染强调，应提升为 token 或替换为已有 token。
- 空、加载和错误状态都必须保留阅读工作区布局。

## 视觉 QA

- 无 `documentId` 的空路由。
- 已选择文档的加载状态。
- 包含标题、表格、代码、列表和 Mermaid 的 Markdown 文档。
- 窄屏无全局横向滚动。
