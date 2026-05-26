# RAG Chat 页面设计覆盖

- 页面路由：`/`、`/rag-chat`
- 页面类型：`Conversation Workbench`
- 源文件：
  - `src/LightRAGNet.React/src/features/rag-chat/RagChatWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/rag-chat/ChatPane.tsx`
  - `src/LightRAGNet.React/src/features/rag-chat/AssistantMessage.tsx`
  - `src/LightRAGNet.React/src/features/rag-chat/QuerySettingsPanel.tsx`
  - `src/LightRAGNet.React/src/features/rag-chat/QueryDetailsDialog.tsx`
  - `src/LightRAGNet.React/src/features/rag-chat/rag-chat.css`

## 页面角色

RAG Chat 是主要对话工作流。它要保持消息流可读，同时暴露查询设置、引用和工程诊断能力。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `Button`
- `StatusPill`
- `MarkdownRenderer`
- 可行时使用 `ErrorState`

## 允许偏离

- 消息气泡可以使用页面局部布局类。
- composer 可以使用页面专用 grid 行为。
- 在通用诊断表格组件出现前，查询详情可以使用页面专用密集表格。
- 消息引用可以使用专用 link pill 样式。

## 规则

- 对话流保持主扫读路径，设置面板不能视觉上压过消息。
- 详情和检索诊断跟随具体消息。
- 引用链接必须来自结构化元数据，不能来自模型生成的 Markdown 链接。
- Clear History 是破坏性命令，应使用共享 danger 按钮样式。
- streaming 和 disabled 状态必须可见，且不能引发布局位移。

## 视觉 QA

- 空聊天。
- 用户消息和助手消息。
- 流式回答。
- 可点击引用和不可点击引用。
- 桌面端和移动端的查询设置面板。
- 带 raw JSON 和表格的查询详情对话框。
