# LightRAGNet React 设计系统总纲

- 日期：2026-05-26
- 主题标识：`react-anthropic-light-ui-standardization`
- 范围：围绕已确认的 Documents 工作台参考图，统一独立 React 应用的视觉语言、共享组件、页面类型和迁移规则
- 标签：`react`、`design-system`、`anthropic-light`、`ui-standardization`、`components`、`page-archetypes`、`accessibility`

## 背景

当前已确认的视觉参考是 Documents 工作台图片：

- [01-documents-table-preview-drawer.png](../docs/superpowers/visuals/anthropic-light-workbench/01-documents-table-preview-drawer.png)

这张图是独立 React 应用后续 UI 风格的源头。目标不是让所有页面长得一模一样，而是让所有页面都像同一个产品里的东西：安静、密集、工程化、暖色浅色界面、克制的陶土色强调、清晰层级、统一图标和可预测的控件。

已有的壳层重设计规格定义了第一段实现范围：

- [2026-05-25-react-anthropic-light-shell-redesign-design.md](../docs/superpowers/specs/2026-05-25-react-anthropic-light-shell-redesign-design.md)

本文档定义更长期的 React UI 标准，用来指导后续所有页面重设计。

## 目标

1. 把已确认的视觉参考转成可复用的 React UI 标准。
2. 用工程对象表达设计决策：token、组件、布局、状态和验证。
3. 避免每个页面自己写颜色、按钮、表格和间距导致风格漂移。
4. 定义页面类型，让后续页面先选择模板，再进入实现。
5. 保留真实功能和操作密度，不为了贴近静态图添加假控件。
6. 把可访问性、响应式和视觉验证纳入完成标准。

## 非目标

- 不在一个切片里重设计所有 React 页面。
- 不在本轮标准化里加入主题切换。
- 不添加假指标、假筛选、假预览或假动作。
- 没有明确迁移理由时，不替换现有 React 路由架构。
- 不把 LightRAGNet 做成营销落地页风格。
- 不为了统一视觉引入重量级组件框架。

## 设计原则

React 应用应该像一个严肃工作台，而不是宣传册。

UI 优先级：

- 快速扫读
- 密集但可读的表格
- 明确的状态和进度
- 紧凑的命令区
- 克制的层级阴影
- 可预测的页面结构
- 真实数据优先于装饰填充

UI 应避免：

- 在操作页面里放超大 hero 区
- 整体陷入单一紫色、蓝色、米色或橙色
- 卡片套卡片
- 大量装饰性圆角容器
- 在页面局部复制颜色值
- 图标风格不一致
- 文本只有 hover 或 focus 后才看得清

## 视觉语言

### 颜色

使用已确认的 `anthropic-light` token 族作为主色板。

```text
app-bg              #fbfaf6
surface             #fffefa
surface-muted       #f7f3ea
surface-raised      #f0eadf
border              #e5ded2
border-strong       #d7ccbd
text-primary        #191817
text-secondary      #5f5a52
text-muted          #8f887d
accent              #c8552d
accent-strong       #a94221
accent-soft         #f3e2d8
success             #4d8a58
warning             #c6871d
danger              #ce4c34
scrim               rgba(36, 31, 26, .30)
```

规则：

- 页面和壳层背景使用 `app-bg`。
- 主面板、表格、抽屉和菜单使用 `surface`。
- 二级区域和表头使用 `surface-muted`。
- 强调色只用于主操作、当前导航、当前 tab 和选中态。
- 语义色只用于状态、健康度、校验和危险操作。
- 功能页面不要引入新的品牌色；确实需要时先提升为共享 token。

### 字体

使用现有字体栈：

```text
Inter, "Segoe UI", "Microsoft YaHei", Arial, sans-serif
```

规则：

- 不按 viewport 缩放字体。
- `letter-spacing` 保持 `0`。
- 页面标题保持强但克制，一般为 `24px` 到 `28px`。
- 区块标题一般为 `16px` 到 `18px`。
- 表格、导航、工具栏和元数据文本应紧凑、便于扫读。
- 弱化文本默认态也必须可读，不能只靠 hover 或 focus 变清楚。

### 圆角和层级

圆角保持克制：

- 壳层和大面板：`10px` 到 `14px`
- 卡片和表格容器：`8px` 到 `10px`
- 按钮、输入框、导航行、菜单项：`8px`
- pill 和计数器：`999px`

层级规则：

- 页面区块默认不要做成浮动卡片。
- 表格容器、重复卡片、弹出层、抽屉和对话框可以有阴影。
- 抽屉和对话框使用可见遮罩和更强阴影。
- hover 状态应改变颜色、边框或阴影，不应造成布局移动。

## Token 契约

所有共享视觉决策应放在 `src/LightRAGNet.React/src/shared/styles/theme.css`。

必须覆盖的 token 组：

- 应用和壳层表面
- 文本层级
- 边框
- 强调色
- 语义状态色
- 控件表面
- 阴影
- 圆角
- 固定壳层尺寸

功能 CSS 可以定义布局尺寸、grid 模板和页面局部类名，但不应定义新的颜色系统。如果页面需要新颜色或阴影，先判断它是否应该成为共享 token。

允许页面局部定义：

- grid 列宽
- 行高
- canvas 尺寸
- 响应式断点
- 语义 token 不足以表达的数据可视化颜色

禁止页面局部定义：

- 主操作颜色
- 表格边框色
- 通用面板背景
- 通用弱化文本颜色
- 通用按钮阴影
- 替代 body 字体栈

## 设计系统护栏

React 页面新增通用 UI 时，应优先使用共享组件，而不是继续扩展页面局部按钮、面板、pill、表格或 dialog 体系。

护栏规则：

- 页面 CSS 默认不定义根级 `font-family`，应继承 `theme.css` 的全局字体栈。
- 页面 CSS 不新增非白名单硬编码 hex；通用颜色应提升为 token 或使用已有 token。
- 命中 `*__button`、`*__panel`、`*__pill`、`*__table`、`*__dialog`、`*__toolbar`、`*__banner` 等通用 UI 概念时，先检查是否应使用 `Button`、`Panel`、`StatusPill`、`DataTableSurface`、`ConfirmDialog`、`Toolbar` 或 `Banner`。
- 图谱 canvas、文档类型图标、Markdown/code 内容渲染和缓存趋势条等数据可视化颜色可以保留局部实现，但必须在测试白名单里登记。
- 现有页面局部 UI 债务必须有迁移入口，不能静默扩散。

## 组件标准

共享组件是统一 UI 的主要约束方式。页面应优先组合共享组件，再补页面局部结构。

### 已实现共享组件

这些组件当前已经有共享实现，定义通用产品语言：

- `AppLayout`
- `PageHeader`
- `PageTabs`
- `Panel`
- `MetricCard`
- `Banner`
- `Toolbar`
- `Button`
- `ButtonLink`
- `IconButton`
- `SegmentedControl`
- `Field`
- `StatusPill`
- `DataTableSurface`
- `DiagnosticTable`
- `Pagination`
- `EmptyState`
- `ErrorState`
- `ConfirmDialog`
- `ActionMenu`
- `FileTypeIcon`
- `ProgressBar`
- `MarkdownRenderer`

### 目标/待补齐共享原语

这些组件属于设计系统目标，但当前不能写成已实现。页面需要时可以先保留局部实现，并把迁移入口留给后续切片：

- `Drawer`
- 通用 `TextField` / `SelectField`
- 通用 `DataTable`

规则：

- 文本命令使用 `Button`。
- 高频行操作使用 `IconButton`。
- 单行超过三个操作时使用 `ActionMenu`。
- 状态、模式、健康度和任务状态使用 `StatusPill`。
- 密集操作表格使用 `DataTableSurface` 包裹。
- 共享 `Drawer` 补齐后，当前页面工作流的侧边详情应迁向共享 `Drawer`。
- 共享 `Drawer` 补齐前，可以保留页面局部抽屉实现，但必须对齐 token、遮罩、焦点管理和响应式规则，且不能视为页面迁移已完成。
- 破坏性操作使用 `ConfirmDialog`。
- 空状态和错误状态使用 `EmptyState`、`ErrorState`。

### 图标标准

命令、导航、表格操作、状态提示和壳层控件统一使用 `lucide-react`。

规则：

- 不使用 emoji 做 UI 图标。
- 除产品 logo 或库里没有的符号外，不手写一次性 SVG 命令图标。
- 图标尺寸保持稳定：密集控件 `16px` 到 `18px`，标题或卡片中 `20px` 到 `24px`。
- 只有图标的按钮必须有可访问标签。
- 图标默认继承当前文本颜色，只有语义状态需要 token 色。

### 交互标准

所有可点击元素应具备：

- pointer cursor
- hover 反馈
- 可见 focus 状态
- 稳定尺寸
- 不可用时有 disabled 状态

动效应短促、实用：

- 标准过渡：`150ms` 到 `250ms`
- 避免在表格行和控件上使用会影响布局的 scale 效果
- 动态指示器需要尊重 `prefers-reduced-motion`

## 页面类型

每个页面应选择一个主页面类型。页面可以混合多个类型，但主工作流必须清晰。

### 列表工作台

适用：

- Documents
- Cache entries
- 任务历史
- 审计日志
- API Key 列表

结构：

```text
PageHeader
SummaryCards when backed by real data
PageTabs for status or category
Toolbar for search, filters, refresh, and primary action
DataTableSurface
Pagination
Shared Drawer after primitive exists, page-local drawer during transition, or Dialog for row details
```

规则：

- 表格行高保持稳定。
- 筛选必须对应已实现行为。
- 汇总卡片只能展示真实计数。
- 行操作应紧凑、可预测。
- 加载、空、错误和部分失败状态不能让表格布局塌掉。

### 表单工作台

适用：

- 文档上传
- 设置页
- 连接配置
- 查询配置页

结构：

```text
PageHeader
Panel or two-column workbench
Validation summary
Primary form controls
Dense selected/preview list when relevant
Action bar
```

规则：

- 表单控件必须有 label，placeholder 不能替代 label。
- 校验错误应靠近相关输入，必要时也显示汇总。
- 主操作清晰且只出现一次。
- 次要操作可见但更安静。
- 长表单按意图分组，不按后端 DTO 形状机械分组。

### 阅读和预览工作区

适用：

- 文档预览
- Markdown 预览
- 转换产物预览
- 查询详情阅读视图

结构：

```text
PageHeader or DrawerHeader
Metadata panel
Content panel
Footer actions when needed
```

规则：

- 内容可读性优先于最大密度。
- 元数据桌面端用紧凑双列，移动端单列。
- 代码、Markdown、表格和图表使用共享渲染器样式。
- 下载或打开动作只有在安全 API 支持时才出现。

### 诊断工作台

适用：

- 系统状态
- 检索诊断
- 查询详情
- 缓存测量证据

结构：

```text
PageHeader
Health summary
Fix-first or priority panel
Evidence panels
Raw JSON/details panel
```

规则：

- 状态严重程度不能只靠颜色表达。
- 证据应便于复制或检查。
- 原始数据放在折叠或二级面板，不进入主扫读路径。
- 修复动作应明确，并靠近对应问题。

### 图谱工作区

适用：

- 知识图谱工作台
- 实体关系探索

结构：

```text
PageHeader or compact graph header
Graph canvas
Floating graph controls
Search/query panel
Legend
Properties drawer or side panel
Dialogs for merge/edit/delete
```

规则：

- 图谱 canvas 可以保持专用视觉风格。
- 周边控件仍使用共享 token 和组件。
- 浮动控件在 canvas 上必须可读。
- 全屏模式不能丢失搜索、布局、图例和退出控件。

### 对话工作台

适用：

- RAG Chat
- 查询助手流程

结构：

```text
PageHeader
Two-column layout on desktop
Chat pane
Settings panel
Message details dialog
Reference preview links
Composer
```

规则：

- 对话是主扫读路径。
- 设置可见，但不能比消息流更抢眼。
- 诊断信息默认跟随消息，不常驻主界面。
- 引用由结构化元数据渲染，不依赖模型生成的链接文本。

## 页面迁移顺序

按这个顺序推进，降低风险并尽早沉淀复用模式。

1. 完成已有壳层重设计规格中的 shell 和文档工作流页面。
2. 将 RAG Chat 标准化为对话工作台。
3. 将 System Status 标准化为诊断工作台。
4. 将 Cache Management 标准化为列表与诊断混合工作台。
5. 在保留图谱交互行为的前提下，标准化 Knowledge Graph 周边控件。
6. 当用户、API Keys、Settings、Audit Logs 等管理页面进入 React 后，再按本标准迁移。

每个页面迁移都必须包含：

- 明确的行为保留清单
- 使用的共享组件
- 仍允许存在的页面局部 CSS
- 覆盖状态：loading、empty、error、success、disabled、pending
- 桌面和移动端视觉检查
- 关键行为的单元或集成测试证据

## 开发者迁移流程

每迁移一个页面：

1. 识别页面类型。
2. 编辑前列出现有用户可见行为。
3. 用共享组件替换页面局部视觉原语。
4. 把重复颜色、边框、阴影和控件样式移动到 token 或共享类。
5. 页面 CSS 只保留布局和功能特定几何关系。
6. 验证所有状态分支。
7. 至少截一张桌面图和一张窄屏图。
8. 对照 Documents 工作台参考图检查整体气质和密度。

这套流程故意做得机械一点。目的就是让 UI 工作更像工程，而不是品味拉扯。

## 可访问性和响应式规则

最低要求：

- 只有图标的操作有可访问标签
- 表单控件有 label
- focus 状态可见
- 状态表达不能只靠颜色
- 默认态文本对比度足够阅读
- `375px`、`768px`、`1024px`、`1440px` 下控件不互相遮挡
- 移动端没有全局横向滚动，表格或图谱内部滚动除外
- 固定工具栏、抽屉和弹出层不能遮住必要操作

响应式规则：

- 窄屏下侧边栏可以折叠或堆叠，但导航必须可达。
- 密集表格可以内部横向滚动，不要强行压缩所有列。
- 抽屉移动端变成全屏或接近全屏。
- 双列工作台移动端折叠为单列。
- 图标操作组窄屏下可以移入 `ActionMenu`。

## 视觉 QA 清单

页面验收前必须确认：

- 通用颜色、边框、阴影和圆角来自共享 token。
- 使用共享按钮、图标按钮、面板、状态 pill 和表格表面。
- 没有 emoji 图标。
- 没有页面局部 body 字体。
- 没有一次性主操作颜色。
- 默认文本对比度可读。
- hover 状态稳定，不引发布局位移。
- 有 loading、empty、error 和 pending 状态。
- focus 状态可见。
- 常见 viewport 下无不合理遮挡。
- 仍保留原有页面行为。

## 实现边界

这份标准指导实现，但不要求一次性大重写。

允许的首轮改动：

- token 清理
- 两个以上页面使用到的模式抽成组件
- 替换局部按钮和状态 badge
- 替换局部表格表面
- 修复明显间距和对比度漂移
- 补齐缺失状态渲染

标准化切片里避免：

- 改后端契约
- 改图谱核心算法
- 改 SignalR 行为
- 改上传或删除语义
- 改 RAG 查询行为
- 没有明确迁移理由时改页面路由

## 验证要求

每个实现切片至少运行：

```powershell
npm test --prefix src/LightRAGNet.React -- --run
npm run build --prefix src/LightRAGNet.React
```

视觉工作还需要浏览器检查变更页面：

- 桌面约 `1440px`
- 平板约 `768px`
- 移动端约 `375px`

截图应覆盖：

- 壳层布局
- 页面头部
- 主工作流
- 变更过的覆盖层或抽屉
- 可行时覆盖 loading、empty、error 状态

## 验收标准

- Documents 工作台参考图可以作为后续 React 页面评审基线。
- 共享 token 覆盖通用视觉系统。
- 共享组件覆盖通用控件和页面表面。
- 每个 React 页面都能映射到一种页面类型。
- 新页面工作从页面类型和共享组件开始，而不是从局部样式开始。
- 页面迁移必须先保留行为，再接受视觉 polish。
- 视觉 QA 和可访问性检查成为完成定义的一部分。

## 未决事项

当前没有阻塞后续计划编写的产品决策。未来实现计划应选择一个页面切片，列出页面类型，并明确使用哪些共享组件和 token。
