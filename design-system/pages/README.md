# 页面覆盖规则

本目录用于保存页面级设计系统覆盖规则。

默认以 [`../MASTER.md`](../MASTER.md) 作为全局唯一源头。只有当某个页面确实需要偏离全局设计系统时，才添加页面覆盖文件。

推荐命名：

```text
documents.md
rag-chat.md
knowledge-graph.md
system-status.md
cache-management.md
document-preview.md
```

页面覆盖文件应保持小而具体，主要说明：

- 页面类型
- 相对 `MASTER.md` 的允许偏离
- 必须使用的共享组件
- 页面特定的响应式或可访问性规则
- 视觉 QA 要点

## 页面局部 UI 债务登记

页面覆盖文件应记录本页面允许保留的局部 UI 体系，并说明后续替换目标。

推荐格式：

```text
后续替换点：
- `<page>__button` -> `Button`
- `<page>__panel` -> `Panel`
- `<page>__pill` -> `StatusPill`
- `<page>__table` -> `DataTableSurface` 或 `DiagnosticTable`
- `<page>__dialog` -> `ConfirmDialog` 或共享 modal/dialog 基础

允许保留：
- 数据可视化几何关系
- canvas 或图谱专用控件位置
- Markdown/code 内容排版
- 消息气泡、composer、引用 pill 等页面工作流专用布局
```
