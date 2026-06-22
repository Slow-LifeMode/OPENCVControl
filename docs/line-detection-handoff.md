# WPF 直线检测算子交接说明

更新时间：2026-06-22

## 当前上下文

当前工作集中在 `D:\Aopencv\1\MyControl-master` 的 WPF Demo 直线检测模块，目标是用 OpenCV 实现类似 Halcon 卡尺测量的直线检测逻辑，并将 `D:\Aopencv\new` 中较新的 WPF UI 结构适配到当前项目。

本阶段只处理直线检测相关逻辑、图像预处理缓存和 WPF Demo 显示行为。WinForms Demo 没有作为主要目标，但底层控件库 `OpenCvWindowTool` 的公共行为会同时影响 WinForms 和 WPF 宿主。

2026-06-22 阶段结论：`OpenCvWindowToolWpfDemo` 的核心 UI 文件已和 `D:\Aopencv\new` 对齐；当前项目自己的 `LineDetectionOperator` 检测核心被保留，没有用 `new` 中另一套检测逻辑覆盖。

## 关键文件

- `OpenCvWindowTool/LineDetectionOperator.cs`：直线检测核心算子，包括卡尺采样、边缘点筛选、全局一致性选点、直线拟合和 ROI 裁剪。
- `OpenCvWindowTool/LineDetectionImageContext.cs`：导入阶段生成的灰度图缓存，供直线检测同步复用。
- `OpenCvWindowTool/LineDetectionOverlayBuilder.cs`：直线检测预览和结果叠加层，包括卡尺分割线、箭头、红色检测点、绿色结果线段。
- `OpenCvWindowTool/RoiItem.cs`：ROI 数据、绘制、命中测试、拖拽控制点。
- `OpenCvWindowTool/OpenCvViewerAction.cs`：鼠标交互、ROI 选中状态、移动/缩放、单 ROI 管理、灰度缓存、叠加层刷新。
- `OpenCvWindowToolWpfDemo/MainWindow.xaml.cs`：WPF Demo 视图桥接层，负责文件对话框、Viewer 事件和检测刷新。
- `OpenCvWindowToolWpfDemo/ViewModels/MainWindowViewModel.cs`：WPF Demo 模块状态、参数状态、结果文本和命令。

## 已完成事项

### 直线检测流程

1. 图像输入后会转为 8 位单通道灰度图，支持单通道、BGR、BGRA 和其它可转换格式。
2. ROI 支持普通矩形和旋转矩形，通过 `RoiItem.ToLineDetectionFrame()` 转成检测坐标框。
3. 按 `SampleCount` 沿排列方向生成多条卡尺线。
4. 每条卡尺线沿搜索方向采样，沿卡尺宽度方向做灰度均值，形成一维灰度曲线。
5. 支持一维梯度和 Sobel 两种边缘强度来源。
6. 对一维曲线做平滑，然后按阈值、极性、局部峰值筛出候选边缘点。
7. 使用全局一致性逻辑从每条卡尺候选点中选择更像同一条直线的一组点。
8. 保留两种直线拟合方式：`LeastSquares` 使用 `DistanceTypes.L2`，`Robust` 使用 `DistanceTypes.Welsch`。
9. 拟合得到的是无限直线，最终会和 ROI 四边形求交，显示为 ROI 内部线段。
10. `LineDetectionResult` 会记录检测耗时，WPF 结果面板会显示毫秒级耗时。

### 图像预处理和缓存

1. `OpenCvViewerAction.SetImage(...)` 是当前显示图像和检测输入的统一入口。
2. 导入图像时调用 `LineDetectionImageContext.ToGray(...)` 转为 8 位单通道灰度图，Viewer 显示和保存的也是该灰度图。
3. `OpenCvViewerAction` 会创建并持有 `LineDetectionImageContext`，避免拖动 ROI 或调整参数时重复做灰度转换。
4. `OpenCvViewerAction.DetectLine(...)` 调用 `LineDetectionOperator.Detect(LineDetectionImageContext, RoiItem, LineDetectionParams)`。
5. `LineDetectionOperator.Detect(Mat, ...)` 仍保留兼容入口，但内部会临时创建 `LineDetectionImageContext`。

### 边缘选择语义

1. `LineSelectionMode.First` 和 `LineSelectionMode.Last` 必须严格按扫描方向解释。
2. 从左到右检测时，`First` 表示每条卡尺扫描路径上最先遇到的有效边缘，不表示强度最强边缘。
3. 从右到左、从上到下、从下到上时，同样以对应扫描方向的先后顺序为准。
4. `Strongest` 才使用强度优先的一致性选点逻辑。

### ROI 交互状态

1. ROI 默认颜色为 `DeepSkyBlue`。
2. ROI 被选中时仍然保持蓝色，只显示可操作控制点。
3. 鼠标点击 ROI 本体外部时，立即取消选中并刷新画布，控制点在 `MouseDown` 当下消失。
4. 命中判断已拆分为两层：点击选中先用 `ContainsBody(...)` 判断 ROI 本体，外部控制点不再吞掉外部点击。
5. 单 ROI 添加逻辑已生效，新增 ROI 会清掉旧 ROI。

### WPF 模块规则

1. WPF Demo 已按 `Controls/`、`ViewModels/`、`Infrastructure/` 拆分，`MainWindow.xaml.cs` 只做 Viewer 和对话框桥接。
2. 输入、ROI、参数、结果四个模块共用一个 Viewer 显示面，不再按模块切换不同图像控件。
3. 参数变化、扫描方向变化和切换到参数模块会立即请求同步检测。
4. 非参数模块会刷新卡尺预览，但不会执行完整检测。
5. ROI 移动过程中只刷新预览；移动完成后执行检测。
6. 检测失败时 ROI 显示为红色；检测成功时 ROI 保持蓝色，结果线段为绿色。
7. WPF 参数面板使用 `Controls/NumericInputBox`，根目录旧版 `NumericInputBox.xaml` 和 `NumericInputBox.xaml.cs` 是迁移前遗留文件，后续可在确认无外部引用后清理。

### 卡尺显示和结果显示

1. 卡尺线按 `SampleCount` 等间距生成。
2. 卡尺线使用完整 `scanLength / 2`，从 ROI 一侧贯穿到另一侧。
3. 箭头通过 `AddArrow(...)` 画在卡尺线尾端，箭头尖端是 `end` 点，方向为 `start -> end`。
4. 箭头翼线长度限制为 `Math.Min(7f, halfScan * 0.25f)`，避免箭头过长或越界。
5. 检测点用红色十字显示。
6. 检测成功线段用绿色显示，并裁剪到 ROI 四边形内。
7. 检测失败时参数模块显示红色 ROI，不显示越界结果线段。

## 当前算法底层逻辑

直线检测主体可以理解为：

```text
输入图像
  -> 转灰度
  -> ROI 转检测坐标框
  -> 按检测点数生成卡尺
  -> 每条卡尺做二维到一维的平均灰度投影
  -> 计算一维梯度或方向 Sobel
  -> 按阈值、极性、局部峰值提候选点
  -> 全局一致性选点
  -> OpenCV FitLine 拟合
  -> 与 ROI 四边求交裁剪
  -> 叠加显示检测点和线段
```

卡尺点不是直接用整幅图找线，而是在每个小矩形卡尺内把宽度方向灰度平均成一条一维曲线，再从这条曲线上找边缘峰值。这样做的目的，是降低单个像素噪声对边缘定位的影响。

## 后续优化方向

### 检测稳定性

1. 增加 ROI 内局部预处理选项，例如高斯滤波、中值滤波、CLAHE、形态学开闭运算。
2. 增加卡尺曲线质量评估，例如峰宽、峰值显著性、前后灰度差、局部信噪比。
3. 候选点选择从单一强度优先，升级为综合评分：强度、极性、距离假设线、连续性、邻近卡尺一致性。
4. 对白带边缘等弱边缘场景，增加“带状目标两侧边缘”模式，而不是只抓单边边缘。
5. 支持亚像素边缘定位的更严格插值，例如三点抛物线或梯度零交叉附近插值。

### 拟合和抗噪

1. 在 `FitLine` 前增加 RANSAC 或 Theil-Sen 风格的外点剔除。
2. 对 `Robust` 模式暴露更多参数，避免固定 `Welsch` 在不同图像上表现不稳定。
3. 增加拟合质量输出，例如残差均值、最大残差、内点比例、连续卡尺命中比例。
4. 检测失败条件不要只看点数，也要看拟合残差和点分布跨度。

### UI 和交互

1. 将当前方形控制点替换为角柄显示，减少视觉干扰。
2. 把 ROI 选中态、编辑态、检测失败态拆成显式状态机，避免颜色和操作点逻辑互相影响。
3. 参数页检测触发可进一步去抖，例如参数连续输入时延迟 150-300ms 后检测。
4. 提供“只预览卡尺/执行检测/冻结结果”三种状态，便于调参。
5. 增加调试视图：显示每条卡尺的一维灰度曲线、梯度曲线和最终选中的边缘点。

### 代码结构

1. 将 `LineDetectionOperator` 中的卡尺采样、候选点提取、全局选点、拟合裁剪拆成更小类，便于单元测试。
2. 给 `ClipLineToFrame(...)`、`BuildAveragedProfile(...)`、`SelectGloballyConsistentPoints(...)` 增加独立测试。
3. 将颜色常量集中管理，避免 WPF 参数页和底层叠加层重复定义颜色。
4. 当前项目没有 README 和自动化测试入口，后续建议补充最小运行说明和测试项目。

## 验证命令

```powershell
dotnet build D:\Aopencv\1\MyControl-master\MyControlTest.sln -v:minimal
```

验证日期：2026-06-22。结果：构建成功，`0 个警告，0 个错误`。
