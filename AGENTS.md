# AGENTS.md

## 项目定位

这是 OpenCV 直线查找算子 Demo。当前主要维护目标是 `OpenCvWindowTool` 控件库和 `OpenCvWindowToolWpfDemo` WPF Demo。

## 维护规则

- 保留当前项目的 `LineDetectionOperator` 检测核心，不要为了套用 `D:\Aopencv\new` 的 UI 而整文件覆盖检测逻辑。
- `LineSelectionMode.First` 和 `LineSelectionMode.Last` 必须严格按扫描方向解释，不能退化为强度优先选择。
- 图像导入阶段通过 `LineDetectionImageContext.ToGray(...)` 转灰度，检测阶段复用 `LineDetectionImageContext`，不要在交互检测路径里重复做灰度转换。
- WPF Demo 使用 MVVM 结构：`ViewModels/MainWindowViewModel.cs` 管理状态和命令，`MainWindow.xaml.cs` 只做 Viewer、事件和文件对话框桥接。
- 修改 WPF UI 时优先维护 `Controls/`、`ViewModels/`、`Infrastructure/` 下的新结构。
- 项目根目录旧版 `OpenCvWindowToolWpfDemo/NumericInputBox.xaml` 和 `.xaml.cs` 是遗留文件；确认无外部引用后再清理，不要影响 `Controls/NumericInputBox`。

## 验证命令

```powershell
dotnet build D:\Aopencv\1\MyControl-master\MyControlTest.sln -v:minimal
```
