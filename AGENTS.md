# AGENTS.md

## 项目定位

这是 OpenCV 直线查找算子 Demo。当前主要维护目标是 `OpenCvWindowTool` 控件库和 `OpenCvWindowToolWpfDemo` WPF Demo。

## 维护规则

- 当前项目的 `LineDetectionOperator` 已按 `SciFindLine.dll` 风格重写；后续改动以这套 rake/caliper 流程和参数语义为准，不再回退到旧版一致性选点实现。
- `LineSelectionMode.First` 和 `LineSelectionMode.Last` 必须严格按扫描方向解释，不能退化为强度优先选择。
- 图像导入阶段通过 `LineDetectionImageContext.ToGray(...)` 转灰度，检测阶段复用 `LineDetectionImageContext`，不要在交互检测路径里重复做灰度转换。
- WPF Demo 使用 MVVM 结构：`ViewModels/MainWindowViewModel.cs` 管理状态和命令，`MainWindow.xaml.cs` 只做 Viewer、事件和文件对话框桥接。
- 修改 WPF UI 时优先维护 `Controls/`、`ViewModels/`、`Infrastructure/` 下的新结构。
- WPF 参数面板当前已对齐原 SciFindLine 界面的大部分核心参数：方向、极性、边缘类型、拟合方式、边缘强度、搜索线数目、边缘宽度、投影宽度、剔除比例、剔除距离、搜索线编号、显示搜索线。
- 项目根目录旧版 `OpenCvWindowToolWpfDemo/NumericInputBox.xaml` 和 `.xaml.cs` 是遗留文件；确认无外部引用后再清理，不要影响 `Controls/NumericInputBox`。

## 验证命令

```powershell
dotnet build D:\Aopencv\OpencvControl\MyControlTest.sln -v:minimal
```
