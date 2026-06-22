# OPENCV_LineFind

OpenCV 直线查找算子 Demo，当前主线集中在 `OpenCvWindowTool` 控件库和 `OpenCvWindowToolWpfDemo` WPF 演示程序。

## 项目结构

- `OpenCvWindowTool/`：WinForms 图像显示控件、ROI 交互、直线检测算子和叠加层绘制。
- `OpenCvWindowToolWpfDemo/`：WPF 演示程序，使用 MVVM 管理输入、ROI、参数和结果面板。
- `OpenCvWindowToolDemo/`：WinForms 演示程序。
- `docs/line-detection-handoff.md`：直线检测和 WPF UI 当前交接说明。

## 当前状态

- WPF Demo 已接入 `D:\Aopencv\new` 中较新的 UI 结构。
- 当前项目自己的 `LineDetectionOperator` 检测核心被保留，没有被 `new` 中另一套检测逻辑覆盖。
- 图像导入阶段会转为 8 位单通道灰度图，并通过 `LineDetectionImageContext` 缓存给直线检测复用。
- `LineSelectionMode.First` 和 `LineSelectionMode.Last` 仍按扫描方向语义选择边缘。

## 构建

```powershell
dotnet build D:\Aopencv\1\MyControl-master\MyControlTest.sln -v:minimal
```

验证日期：2026-06-22。结果：构建成功，`0` 警告，`0` 错误。
