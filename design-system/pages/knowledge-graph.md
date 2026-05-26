# Knowledge Graph 页面设计覆盖

- 页面路由：`/graph-view`
- 页面类型：`Graph Workspace`
- 源文件：
  - `src/LightRAGNet.React/src/features/graph-workbench/GraphWorkbench.tsx`
  - `src/LightRAGNet.React/src/features/graph-workbench/graph-workbench.css`
  - `src/LightRAGNet.React/src/components/graph/*.tsx`

## 页面角色

Knowledge Graph 是沉浸式图谱工作区。图谱 canvas 可以保留专用视觉，但周边控件仍必须遵循共享设计系统。

## 必须使用的共享组件

- `Button`
- `IconButton`
- `Panel`
- 可行时使用 `Toolbar`
- `StatusPill`
- `ErrorState`
- `ConfirmDialog`

## 允许偏离

- Sigma canvas 颜色、节点颜色、边颜色、布局控件和图例颜色可以保持图谱专用。
- 浮动控件位置可以继续由图谱工作区局部控制。
- 图谱 canvas 可以在应用壳层内全幅显示。

## 规则

- 浮动控件在 canvas 上必须保持可读。
- 搜索、查询、布局、设置、图例和属性面板使用暖色浅色表面、边框、圆角和阴影 token。
- 图谱编辑和合并对话框应逐步迁移到共享 dialog 原语。
- 全屏模式必须保留必要控件和明显退出方式。
- canvas 和控件必须做视觉验证，因为单元测试无法发现空白图谱或遮挡问题。

## 视觉 QA

- 初始图谱加载。
- 空或错误 overlay。
- 搜索结果弹层。
- 布局菜单和设置面板。
- 图例打开和关闭。
- 含长属性值的属性面板。
- merge、edit、delete 和 confirm 对话框。
