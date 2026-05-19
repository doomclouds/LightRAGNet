# MudFileUpload CustomContent Picker Regression

- Date: `2026-05-19`
- Topic slug: `mudfileupload-customcontent-picker`
- Status: `Captured`
- Scope: `UI`
- Tags: `MudBlazor`, `file-upload`, `migration`, `Blazor`

## Symptom

点击上传页的 “Select Files (Multiple)” 按钮后，浏览器不再弹出文件选择窗口。页面没有明显报错，按钮看起来也不是禁用状态。

## Trigger / Context

- 项目升级到 MudBlazor 9 后出现。
- 上传页使用 `MudFileUpload` 的 `CustomContent` 自定义按钮。
- 旧写法依赖 `MudButton HtmlTag="label"` 触发内部 file input。

## Root Cause

MudBlazor 9 的 `MudFileUpload.CustomContent` 不再自动把点击转发到内部文件选择器。自定义内容需要通过 context 显式调用 `OpenFilePickerAsync()`；只把按钮渲染成 `label` 不再能稳定关联内部 input。

## Fix

- 在 `CustomContent` 上声明 `Context="fileUpload"`。
- 在自定义按钮上调用 `OnClick="@fileUpload.OpenFilePickerAsync"`。
- 去掉误导性的 `HtmlTag="label"`。
- 增加上传页 markup 回归测试，锁住 MudBlazor 9 的显式 picker 调用约定。

## Why This Fix

显式调用 `OpenFilePickerAsync()` 是 MudBlazor 9 的组件合同，优于继续依赖 label/input 的隐式 DOM 关系。这个修复把行为绑定到组件 API 上，减少后续样式调整、DOM 结构变化或组件升级时再次失效的风险。

## Recognition Clues

- 文件上传按钮可点击，但系统文件选择框不出现。
- `MudFileUpload` 内有 `<CustomContent>`，但没有 `Context="..."`。
- 自定义按钮只有 `HtmlTag="label"`，没有调用 `OpenFilePickerAsync()`。
- 问题常出现在 MudBlazor 8 到 9 的升级后。

## Applicability / Non-Applicability

### Applies When

- Blazor 页面使用 MudBlazor 9 的 `MudFileUpload`。
- 上传入口通过 `CustomContent` 自定义按钮、图标或拖拽区域。
- 文件选择框不弹出，但页面没有服务端上传错误。

### Does Not Apply When

- 使用 `MudFileUpload` 默认内容而非 `CustomContent`。
- 文件选择框可以打开，但 `OnFilesChanged` 不触发；那应检查校验、`SuppressOnChangeWhenInvalid`、同名文件重复选择或浏览器限制。
- 页面整体没有交互；那应优先检查 Blazor render mode、SignalR 连接或 JS 资源加载。

## Related Artifacts

- Spec: `None yet.`
- Plan: `None yet.`
- Archive: `None yet.`
- Related Problems:
  - `None yet.`
- Code or Test:
  - [MarkdownUpload.razor](../../../../src/LightRAGNet.Web/Components/Pages/MarkdownUpload.razor)
  - [MarkdownUploadMarkupTests.cs](../../../../tests/LightRAGNet.Tests/Web/MarkdownUploadMarkupTests.cs)
