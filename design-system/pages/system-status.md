# System Status 页面设计覆盖

- 页面路由：`/system-status`
- 页面类型：`Compact Diagnostic Workbench`
- 源文件：
  - `src/LightRAGNet.React/src/features/system-status/SystemStatusWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/system-status/system-status.css`
- 主参考：
  - `docs/superpowers/visuals/anthropic-light-workbench/04-system-cache-table-pages.png`
  - `docs/superpowers/visuals/anthropic-light-workbench/05-system-status-compact-diagnostics-workbench-react-prototype.html`

## 页面角色

System Status 是精炼诊断工作台。页面先展示整体健康、刷新动作和摘要，再进入证据表、修复优先级、功能影响，最后把 raw JSON 放在二级辅助区域，避免让原始响应抢占主扫读路径。

## 必须使用的共享组件

- `PageHeader`
- `Panel`
- `Button`
- `StatusPill`
- `DataTableSurface`

`DiagnosticTable`、`Banner` 当前不是本页必需依赖；后续如果 Cache Management 或 RAG Chat 复用相同诊断表格与提示模式，再评估是否提升为共享接入。

## 页面局部组件

- `SystemStatusSummaryTiles`
- `SystemStatusEvidenceTable`
- `SystemStatusRemediationPanel`
- `SystemStatusFeatureImpactPanel`
- `SystemStatusRawJsonPanel`

## 允许偏离

- dashboard grid 和 summary tile 的页面布局可以保留，用于压缩首屏诊断密度。
- raw JSON 的固定尺寸、滚动和 monospace 代码呈现可以保留，因为它属于原始诊断数据阅读需求。
- evidence table 的字段组织、紧凑行距和主扫读顺序可以保留，但表面容器必须保持 `DataTableSurface` 体系。

## 规则

- 图标使用 `lucide-react`，不要引入页面私有 SVG 图标体系。
- 不得为了演示视觉而制造假指标、假 API 字段或前端推断字段；展示内容必须来自当前接口或明确的 UI 派生状态。
- Evidence table 是诊断细节的主扫读路径，字段命名、状态 pill 和长值处理必须优先服务快速定位问题。
- raw JSON 是二级辅助信息，不能重新变成页面主内容。
- 页面 CSS 不定义根级字体栈，继承全局字体与 token。
- 页面 CSS 不复制通用 button、panel、pill、table、dialog 体系；这些能力优先走共享组件。

## 视觉 QA

- 桌面视口必须同时检查 summary、evidence、side panels 和 raw JSON 的层级与密度。
- `768px` 视口检查 summary tiles、evidence table、修复优先级和功能影响的换行顺序。
- `375px` 视口检查标题操作、summary tiles、证据行、side panels 和 raw JSON 滚动不互相挤压。
- 检查 loading、refresh pending、API error、empty/not measured states。
