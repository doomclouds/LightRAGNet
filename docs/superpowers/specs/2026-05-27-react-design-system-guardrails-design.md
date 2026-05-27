# React Design System Guardrails Design

- 日期：2026-05-27
- 主题标识：`react-design-system-guardrails`
- 范围：`LightRAGNet.React` 设计系统共享组件、约束测试和页面迁移入口
- 标签：`react`、`design-system`、`guardrails`、`anthropic-light`、`components`、`visual-qa`

## 背景

当前 React UI 已经有明确方向，而不是缺少视觉概念：

- `design-system/MASTER.md` 定义了 `anthropic-light` 总纲。
- `design-system/pages/*.md` 定义了页面覆盖规则。
- Documents 工作台已经作为参考实现沉淀。
- `src/LightRAGNet.React/src/shared/styles/theme.css` 和 `app.css` 已经包含基础 token、shell 和部分共享类。
- `src/LightRAGNet.React/src/shared/components/` 已经有 `PageHeader`、`Panel`、`Button`、`IconButton`、`StatusPill`、`DataTableSurface`、`Pagination`、`MarkdownRenderer` 等组件。

现阶段风险不在于“再设计一套更好看的 UI”，而在于设计系统执行约束不足。部分页面仍保留局部按钮、面板、pill、表格、dialog、字体栈和少量硬编码颜色。如果继续让页面各自扩展局部视觉体系，`MASTER.md` 会退化成建议文档，后续迁移会变成反复补锅。

本轮采用“严格收束”：补齐缺失共享原语，同时加入测试和源文件扫描规则，防止新的页面局部 UI 体系继续扩散。本轮不迁移完整 P1 页面，不改变后端契约，也不重做业务交互。

## 目标

1. 补齐当前设计系统缺失或不完整的共享组件。
2. 让页面新增通用 UI 时优先使用共享组件，而不是继续写局部 `button`、`panel`、`pill`、`table`、`dialog` 体系。
3. 用测试约束设计系统关键 token、组件 class 和页面 CSS 边界。
4. 明确哪些硬编码颜色、局部 CSS 和页面专用几何关系允许保留。
5. 为 `System Status`、`Cache Management`、`RAG Chat`、`Knowledge Graph` 和 `Document Preview` 标出后续迁移入口。
6. 保持本轮改动可验证、低风险、可作为后续页面迁移前置基础。

## 非目标

- 不在本轮完整迁移 `System Status`、`Cache Management`、`RAG Chat`、`Knowledge Graph` 或 `Document Preview`。
- 不修改 API、SignalR、图谱算法、文档上传、预览或缓存清理语义。
- 不引入新的组件框架。
- 不增加主题切换。
- 不为了通过规则删除所有页面局部 CSS。
- 不把图谱 canvas 颜色、文档类型图标颜色、代码高亮和数据可视化颜色强行纳入通用语义 token。

## 设计原则

这轮要把设计系统从“文档标准”推进到“工程约束”。

优先级：

- 共享组件表达通用控件语法。
- 页面 CSS 只负责页面布局、工作流几何关系和确实专用的数据可视化。
- 颜色、字体、边框、阴影、圆角、focus、disabled 和 hover 尽量由共享 token 和共享组件控制。
- 测试规则要防止新债务，但不能误伤当前合理的专用视觉。
- 迁移入口要明确，让下一轮页面改造知道从哪里下手。

避免：

- 因为新增规则而一次性大改所有页面。
- 为了抽象而抽象，把组件做成大而全的业务容器。
- 用正则测试假装能判断所有设计质量。
- 把设计系统护栏写得过硬，导致图谱、Markdown、文件类型图标等合理例外无法维护。

## 共享组件范围

本轮新增或完善这些共享原语。

### Banner

用途：

- 页面级成功、错误、警告、信息提示。
- 操作结果反馈，例如 copy 成功、刷新失败、清理完成。

建议 API：

```tsx
<Banner tone="error" title="Unable to load cache overview">
  Check the server connection and try again.
</Banner>
```

规则：

- `tone` 支持 `info`、`success`、`warning`、`danger`。
- 必须使用共享语义 token。
- 不使用 emoji 图标。
- 可选图标来自 `lucide-react`。
- 错误状态不能只靠颜色表达，必须有文本。

### ConfirmDialog

用途：

- 删除文档。
- 清理缓存。
- 图谱合并、编辑、删除等破坏性或不可逆操作。

建议 API：

```tsx
<ConfirmDialog
  open={isOpen}
  title="Clear cache entries?"
  tone="danger"
  confirmLabel="Clear"
  cancelLabel="Cancel"
  pending={isPending}
  onConfirm={handleConfirm}
  onCancel={handleCancel}
>
  This action cannot be undone.
</ConfirmDialog>
```

规则：

- 使用共享 `lrn-scrim`、`lrn-modal` 层级。
- `Escape` 和取消按钮关闭，除非 `pending`。
- `pending` 时禁用确认和取消策略必须明确。
- focus 状态可见。
- 破坏性确认使用 danger tone。

### SegmentedControl

用途：

- 时间窗口。
- 查询模式。
- tab 级别以下的小型模式切换。

建议 API：

```tsx
<SegmentedControl
  ariaLabel="Time window"
  value={window}
  options={[
    { value: "24h", label: "24h" },
    { value: "7d", label: "7d" }
  ]}
  onChange={setWindow}
/>
```

规则：

- 用于互斥选择。
- 当前项要有 `aria-pressed` 或等价可访问状态。
- 不用按钮文字之外的颜色作为唯一状态。
- 固定高度，切换不造成布局位移。

### Field

用途：

- 文本输入。
- 数字输入。
- select。
- textarea。
- checkbox/toggle 的 label 包装。

建议 API：

```tsx
<Field label="Workspace" hint="Use _ for the default workspace">
  <input value={workspace} onChange={handleWorkspaceChange} />
</Field>
```

规则：

- label 不能只依赖 placeholder。
- hint 和 error 文本使用明确 id 关联。
- 页面仍可控制具体 input 值和业务校验。
- Field 不负责复杂表单状态管理。

### DiagnosticTable

用途：

- 系统状态证据。
- RAG 查询详情 key-value。
- 缓存测量合同。
- 小型诊断明细。

建议 API：

```tsx
<DiagnosticTable
  rows={[
    { label: "Provider", value: providerName },
    { label: "Latency", value: latencyText }
  ]}
/>
```

规则：

- 支持 key-value 和简单列式 rows。
- 长值可以换行或 `overflow-wrap: anywhere`。
- 适合诊断，不替代业务数据表格。
- 表头、边框、弱化文本和 monospace 值使用共享 token。

## 测试和护栏规则

新增或扩展 Vitest source/CSS 测试，优先放在 `src/LightRAGNet.React/tests/unit/shared/styles/theme.test.ts`，必要时新增专门的 `designSystemGuardrails.test.ts`。

### Token 和共享 class 存在性

测试必须覆盖：

- `theme.css` 中保留 `anthropic-light` token。
- `app.css` 中存在共享组件 class：`lrn-banner`、`lrn-modal`、`lrn-segmented-control`、`lrn-field`、`lrn-diagnostic-table`。
- `Button`、`Panel`、`StatusPill`、`DataTableSurface` 等已有 class 不被意外删除。

### 字体栈规则

页面级 CSS 默认不能定义自己的通用 `font-family`。允许例外：

- `monospace` 用于 code、raw JSON、诊断值或 key prefix。
- 第三方 canvas 或图谱库需要的内部兼容。

当前已知迁移目标：

- `system-status.css` 的页面根字体栈应后续继承全局字体。
- `cache-management.css` 的页面根字体栈应后续继承全局字体。
- `graph-workbench.css` 的页面根字体栈应后续继承全局字体。

本轮测试可以先建立白名单和警告边界，不强制立即清空旧债。

### 硬编码颜色规则

页面 CSS 中新增通用视觉颜色应避免字面 hex。

允许例外：

- `shared/styles/theme.css` 定义 token。
- `shared/styles/app.css` 中现有文档类型图标、品牌 mark、兼容 alias 和语义增强色。
- `graphologyAdapter.ts` 的 sigma canvas palette。
- 文档类型图标颜色。
- Markdown/code 内容渲染中确实需要的局部强调色，但后续优先迁入 `MarkdownRenderer` 共享样式。
- 测试里用于断言历史 dark literal 不存在的字符串。

测试策略：

- 用白名单文件和白名单 selector 控制例外。
- 先防止 P1 页面继续新增非白名单 hex。
- 对现存 `document-preview.css` 字面颜色标记为迁移入口，而不是本轮强删。

### 局部 UI 体系规则

新增页面局部类名若命中这些概念，应优先使用共享组件：

- `*__button`
- `*__icon-button`
- `*__panel`
- `*__pill`
- `*__dialog`
- `*__toolbar`
- `*__table`
- `*__banner`

规则不是禁止所有命名，而是阻止“通用控件局部复制”。允许例外：

- 页面布局容器，例如 `graph-workbench__properties`。
- 数据可视化原语，例如 `cache-rate-bar`、`cache-dot`。
- 业务专用消息气泡、composer、图谱浮层。
- 已存在的页面局部体系在迁移完成前可白名单保留。

测试输出应能告诉开发者：

- 命中的文件。
- 命中的类名。
- 推荐替换的共享组件。
- 是否属于已登记白名单。

## 页面迁移入口

本轮不做完整迁移，但要让后续切片可以按清单推进。

### System Status

后续替换点：

- `system-status__header` -> `PageHeader`
- `system-status__button` -> `Button`
- `system-status__panel` -> `Panel`
- `system-status__error` / `system-status__loading` -> `Banner` 或 `ErrorState`
- 证据表格 -> `DiagnosticTable`
- 局部状态 pill -> `StatusPill`

保留理由：

- 诊断工作台布局和 evidence 展开方式可以继续是页面专用。

### Cache Management

后续替换点：

- `cache-page-head` -> `PageHeader`
- `cache-toolbar` -> `Toolbar`
- `cache-button` -> `Button`
- `cache-segmented` -> `SegmentedControl`
- `cache-field` -> `Field`
- `cache-banner` -> `Banner`
- `cache-panel` -> `Panel`
- `cache-table-wrap` -> `DataTableSurface`
- `cache-pill` -> `StatusPill`

保留理由：

- rate bar、trend bar、family dot 和 cache insight icon 属于缓存数据可视化，可以继续局部实现。

### RAG Chat

后续替换点：

- 查询详情 tab 切换 -> `SegmentedControl` 或共享 tabs 模式。
- 详情 key-value 表 -> `DiagnosticTable`。
- 对话详情弹层 -> `ConfirmDialog` 不适用，应抽共享 `Dialog` 或复用 `lrn-modal` 基础。
- 错误和加载状态 -> `Banner`、`ErrorState`。

保留理由：

- 消息气泡、composer、引用 pill 是对话工作台专用布局，可继续页面局部。

### Knowledge Graph

后续替换点：

- 图谱对话框确认类 -> `ConfirmDialog`。
- 普通操作按钮 -> `Button` / `IconButton`。
- 设置输入 -> `Field`。
- 状态和错误 -> `Banner` / `ErrorState`。

保留理由：

- canvas、浮动控件位置、图例色块、节点/边颜色和布局菜单几何关系属于图谱专用。

### Document Preview

后续替换点：

- 页面内容状态 -> `EmptyState` / `ErrorState`。
- 预览元数据 -> `Panel` + `DiagnosticTable`。
- Markdown 字面颜色 -> 迁移到 `MarkdownRenderer` 或共享 markdown token。

保留理由：

- 阅读内容的宽度、行高、代码块和表格排版可以保持页面专用，但颜色要逐步 token 化。

## 实现边界

本设计批准后，实施计划应拆成小切片：

1. 新增共享组件和 CSS class。
2. 为组件补最小单元测试或 source 测试。
3. 新增设计系统护栏测试，先白名单现存债务。
4. 更新 `design-system/MASTER.md` 或页面覆盖文件，记录新组件和规则。
5. 不迁移完整页面，只在必要时用新组件替换极小范围示例，证明 API 可用。

## 验证要求

最低验证：

```powershell
npm test --prefix src/LightRAGNet.React -- --run
npm run build --prefix src/LightRAGNet.React
```

设计系统护栏测试必须证明：

- 共享组件 class 存在。
- 页面根级 `font-family` 新增会被发现。
- 非白名单页面 hex 会被发现。
- 局部通用 UI 类名会被发现或要求白名单。

如果实现切片触及可见页面，还需要浏览器视觉检查：

- `1440px` 桌面。
- `768px` 平板。
- `375px` 移动端。

## 验收标准

- `Banner`、`ConfirmDialog`、`SegmentedControl`、`Field`、`DiagnosticTable` 作为共享组件存在。
- 新组件使用 `anthropic-light` token，不引入新视觉体系。
- 设计系统护栏测试可运行，并能阻止新的明显局部 UI 债务。
- 现有页面局部债务被白名单化并关联后续迁移入口。
- `design-system` 文档说明新组件、例外边界和页面迁移入口。
- React 测试和构建通过。
- 本轮不改变用户可见业务行为。

## 后续顺序

护栏落地后，页面迁移建议顺序：

1. `System Status`：诊断工作台最适合验证 `Banner`、`DiagnosticTable`、`StatusPill`。
2. `Cache Management`：验证 `Toolbar`、`SegmentedControl`、`Field`、`DataTableSurface`。
3. `RAG Chat`：收束 dialog、诊断表格和状态反馈。
4. `Knowledge Graph`：只标准化周边控件，保留 canvas 专用视觉。
5. `Document Preview`：把 Markdown/阅读样式迁入共享 renderer token。
