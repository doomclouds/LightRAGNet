# React 页面设计审计

- 日期：2026-05-26
- 范围：`src/LightRAGNet.React`
- 标准：[MASTER.md](MASTER.md)

## 摘要

React 应用已经具备 `anthropic-light` 方向的基础：全局 token、共享组件、Lucide 图标和应用壳层。剩余工作主要是标准化，不是重新发明。

推荐优先级：

1. 先把 Documents 做成参考实现。
2. 继续迁移 Cache Management 这类 list/diagnostic workbench 页面。
3. 围绕共享面板、按钮和渲染器行为规范 RAG Chat 与 Document Preview。
4. Knowledge Graph 保留图谱专用视觉，但标准化周边控件。

## 当前页面地图

| 区域 | 路由 | 页面类型 | 当前符合度 | 主要差距 | 优先级 |
| --- | --- | --- | --- | --- | --- |
| Documents | `/documents` | List Workbench | 高 | 仍有文档专用 badge 和局部筛选弹层；最终验收前需确认汇总卡片数据足够真实 | P0 |
| Upload Document | `/documents/upload` | Workbench Form | 高 | dropzone 和已选文件列表是局部实现但可接受；需要移动端视觉验证 | P0 |
| Document Preview Drawer | `/documents` 内部 | Reading And Preview Workspace | 高 | 抽屉和焦点行为较好，后续可沉淀为共享 `Drawer` | P0 |
| Document Preview Page | `/document-preview/:id` | Reading And Preview Workspace | 中 | 页面 CSS 有字面内容颜色；元数据较轻；完整阅读布局需要视觉验证 | P1 |
| RAG Chat | `/`、`/rag-chat` | Conversation Workbench | 中 | 部分 raw `button` + class 组合；诊断表格和 dialog 仍需迁向共享 `DiagnosticTable` / `ConfirmDialog` | P1 |
| System Status | `/system-status` | Compact Diagnostic Workbench | 高 | 已按 compact diagnostics workbench 迁移；后续观察局部诊断组件是否被 Cache/RAG Chat 复用，再决定是否提升共享 | 已迁移 |
| Cache Management | `/cache-management` | List + Diagnostic Workbench | 中 | 布局强，但局部 button/panel/pill/table 系统重复了共享组件能力 | P1 |
| Knowledge Graph | `/graph-view` | Graph Workspace | 中 | 专用 canvas 合理；周边控件和 dialog 大多仍是页面局部体系 | P2 |

## 共享组件覆盖情况

已经可用：

- `PageHeader`
- `PageTabs`
- `Panel`
- `Toolbar`
- `Button`
- `ButtonLink`
- `IconButton`
- `StatusPill`
- `DataTableSurface`
- `Pagination`
- `MetricCard`
- `ProgressBar`
- `FileTypeIcon`
- `ActionMenu`
- `EmptyState`
- `ErrorState`
- `MarkdownRenderer`

本轮已补齐的共享原语：

- `Banner`
- `SegmentedControl`
- `Field`
- `DiagnosticTable`
- 共享 `ConfirmDialog`

缺失或不完整的共享原语：

- `Drawer`
- 通用 `TextField` / `SelectField` 控件（`Field` 包装已补齐）
- 通用 `DataTable`

说明：共享原语状态已更新，但页面迁移仍按后续切片推进；现有页面里的局部表单、诊断表格、banner/alert 或 dialog 实现不因此视为已经完成迁移。

## 标准化风险

- `cache-management.css`、`graph-workbench.css` 仍重复定义了按钮、面板、pill 和表格概念；System Status 的旧 root font、局部 button/panel/status/table debt 已清理，raw JSON monospace 是保留项。
- `document-preview.css` 在 Markdown 内容样式里使用字面颜色，部分应提升为 renderer token 或共享 Markdown 样式。
- Graph 控件使用了正确 token，但仍是独立组件词汇。
- Cache Management 仍需检查页面局部字体、按钮、panel、pill 和 table 是否继承共享体系。
- RAG Chat、Graph 等页面仍有局部控件债务，不能把 System Status 的完成误读为全站完成。

## 推荐迁移切片

### 切片 1：补齐设计系统组件

交付：

- 共享 `Drawer`
- 通用 `DataTable`
- 将已补齐的 `ConfirmDialog`、`Banner`、`SegmentedControl`、`Field`、`DiagnosticTable` 接入需要迁移的页面

验证：

```powershell
npm test --prefix src/LightRAGNet.React -- --run
npm run build --prefix src/LightRAGNet.React
```

### 切片 2：加固 Documents 参考实现

交付：

- 保持 Documents 作为视觉参考
- 用共享组件替换剩余 ad hoc button/link 样式
- 验证汇总卡片和筛选行为
- 如果另一个页面也需要抽屉，则抽取 drawer pattern

视觉检查：

- `/documents`
- `/documents/upload`
- `/document-preview/:id`

### 切片 3：诊断页面标准化

交付：

- 保持 System Status 的 compact diagnostics workbench 作为诊断页参考样式
- 将 Cache Management 中重复的 button、pill、panel、table 迁向共享组件
- 观察 System Status 的 `SystemStatusSummaryTiles`、`SystemStatusEvidenceTable`、`SystemStatusRemediationPanel`、`SystemStatusFeatureImpactPanel`、`SystemStatusRawJsonPanel` 是否能被 Cache Management 或 RAG Chat 复用
- 保留 copy、export、refresh、clear 等行为

视觉检查：

- `/cache-management`
- `/system-status` 作为回归参考，不再作为待迁移项

### 切片 4：对话和图谱打磨

交付：

- 将 RAG Chat dialog 和操作表面迁向共享组件
- 标准化 Graph dialog 和周边控件
- 保持 graph canvas 行为不变

视觉检查：

- `/rag-chat`
- `/graph-view`

## 单页完成门

每个迁移后的页面必须说明：

- 行为保留清单
- 使用的共享组件
- 仍保留的页面局部 CSS 及理由
- loading 状态
- 适用时的 empty 状态
- error 状态
- 操作 pending/disabled 状态
- 桌面截图
- 窄屏截图
- 关键行为的单元或集成测试证据

## 备注

这份审计不是“一次性重写所有页面”的许可。设计系统应该通过小型可复用组件和可验证页面切片逐步变严格。每次迁移一个页面或一个组件族，并在每个切片后留下证据。
