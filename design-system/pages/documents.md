# Documents 页面设计覆盖

- 页面路由：`/documents`、`/documents/upload`
- 页面类型：`List Workbench`、`Workbench Form`
- 源文件：
  - `src/LightRAGNet.React/src/features/documents/DocumentsPage.tsx`
  - `src/LightRAGNet.React/src/features/documents/UploadDocumentPage.tsx`
  - `src/LightRAGNet.React/src/features/documents/DocumentPreviewPanel.tsx`

## 页面角色

Documents 是 `anthropic-light` 工作台风格的参考实现。其他页面迁移时，应优先对照本页面的气质、密度、表格行为、操作区位置和抽屉处理方式。

## 必须使用的共享组件

- `PageHeader`
- `MetricCard`
- `PageTabs`
- `Toolbar`
- `Button`
- `ButtonLink`
- `IconButton`
- `ActionMenu`
- `StatusPill`
- `DataTableSurface`
- `Pagination`
- `ProgressBar`
- `FileTypeIcon`
- `EmptyState`
- `ErrorState`

## 允许偏离

- 文档列表 CSS 可以定义表格列宽、行结构、文件元数据布局、筛选弹层布局和抽屉宽度。
- `DocumentStatusBadge` 在泛用状态映射组件出现前可以继续保持文档专用。
- 文件类型颜色可以继续放在 `FileTypeIcon`，因为它表达的是文档类型，不是品牌色。

## 规则

- 汇总卡片必须来自真实当前列表数据，或明确记录为后端聚合数据。
- 筛选必须映射到已实现的本地或服务端行为。
- 行操作列在 pending 状态下不能产生位移。
- 预览抽屉是标准抽屉参考：遮罩、右侧位置、焦点返回、Escape 关闭、移动端全屏。
- 上传页是工作台表单，不是 hero 风格上传页。

## 视觉 QA

- `/documents`：表格、tabs、筛选、分页、行操作、预览抽屉。
- `/documents/upload`：dropzone、已选文件列表、校验消息、禁用上传状态。
- 移动端：抽屉变全屏，工具栏换行但不隐藏主操作，表格使用内部滚动。
