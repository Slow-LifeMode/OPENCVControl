using OpenCvWindowTool;
using OpenCvWindowToolWpfDemo.Infrastructure;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using PointF = System.Drawing.PointF;

namespace OpenCvWindowToolWpfDemo.ViewModels
{
    /// <summary>
    /// 表示WPF Demo当前显示的功能模块。
    /// </summary>
    public enum OperatorModule
    {
        Input,
        Roi,
        Params,
        Result
    }

    /// <summary>
    /// 表示下拉框显示项和实际值的对应关系。
    /// </summary>
    /// <typeparam name="T">实际值类型。</typeparam>
    public sealed class ComboOption<T>
    {
        /// <summary>
        /// 初始化下拉框选项。
        /// </summary>
        /// <param name="text">显示文本。</param>
        /// <param name="value">实际值。</param>
        public ComboOption(string text, T value)
        {
            Text = text;
            Value = value;
        }

        /// <summary>
        /// 获取显示文本。
        /// </summary>
        public string Text { get; private set; }

        /// <summary>
        /// 获取实际值。
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// 返回显示文本。
        /// </summary>
        /// <returns>显示文本。</returns>
        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// 管理主窗口参数、模块状态和检测结果文本。
    /// </summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private OperatorModule currentModule;
        private LineScanDirection currentDirection;
        private ComboOption<LineEdgePolarity> selectedPolarity;
        private ComboOption<LineSelectionMode> selectedSelectionMode;
        private ComboOption<LineFitMode> selectedFitMode;
        private double threshold;
        private double sampleCount;
        private double edgeWidth;
        private double projectionWidth;
        private double rejectRatio;
        private double rejectDistance;
        private double profileLineIndex;
        private double roiCenterX;
        private double roiCenterY;
        private double roiWidth;
        private double roiHeight;
        private double roiAngle;
        private bool hasRoiGeometry;
        private bool updatingRoiGeometry;
        private bool showSearchLines;
        private string imageStatus;
        private string roiStatus;
        private string liveStatus;
        private string liveLine;
        private string resultStatus;
        private string resultStart;
        private string resultEnd;
        private string resultAngle;
        private string resultPointCount;
        private string resultAverageStrength;
        private string resultMaxStrength;
        private string resultDirection;
        private string resultElapsed;
        private Brush liveStatusBrush;
        private Brush resultStatusBrush;

        /// <summary>
        /// 初始化主窗口视图模型。
        /// </summary>
        public MainWindowViewModel()
        {
            PolarityOptions = new[]
            {
                new ComboOption<LineEdgePolarity>("从黑到白", LineEdgePolarity.Positive),
                new ComboOption<LineEdgePolarity>("从白到黑", LineEdgePolarity.Negative),
                new ComboOption<LineEdgePolarity>("全部", LineEdgePolarity.Any)
            };
            SelectionModeOptions = new[]
            {
                new ComboOption<LineSelectionMode>("第一条", LineSelectionMode.First),
                new ComboOption<LineSelectionMode>("最后一条", LineSelectionMode.Last),
                new ComboOption<LineSelectionMode>("最强边缘", LineSelectionMode.Strongest)
            };
            FitModeOptions = new[]
            {
                new ComboOption<LineFitMode>("局部拟合", LineFitMode.Local),
                new ComboOption<LineFitMode>("最小二乘拟合", LineFitMode.LeastSquares),
                new ComboOption<LineFitMode>("Huber拟合", LineFitMode.Huber)
            };

            currentModule = OperatorModule.Input;
            currentDirection = LineScanDirection.LeftToRight;
            selectedPolarity = PolarityOptions[0];
            selectedSelectionMode = SelectionModeOptions[0];
            selectedFitMode = FitModeOptions[1];
            threshold = 30d;
            sampleCount = 30d;
            edgeWidth = 1d;
            projectionWidth = 1d;
            rejectRatio = 20d;
            rejectDistance = 5d;
            profileLineIndex = 1d;
            showSearchLines = true;
            imageStatus = "未导入图像";
            roiStatus = "未创建ROI";
            liveStatusBrush = Brushes.Red;
            resultStatusBrush = Brushes.Red;

            OpenImageCommand = new RelayCommand(_ => OpenImageRequested?.Invoke());
            CreateRectangleRoiCommand = new RelayCommand(_ => CreateRoiRequested?.Invoke(RoiShape.Rectangle));
            CreateRotatedRectangleRoiCommand = new RelayCommand(_ => CreateRoiRequested?.Invoke(RoiShape.RotatedRectangle));
            ClearRoiCommand = new RelayCommand(_ => ClearRoiRequested?.Invoke());
            ShowInputCommand = new RelayCommand(_ => SetModule(OperatorModule.Input));
            ShowRoiCommand = new RelayCommand(_ => SetModule(OperatorModule.Roi));
            ShowParamsCommand = new RelayCommand(_ => SetModule(OperatorModule.Params));
            ShowResultCommand = new RelayCommand(_ => SetModule(OperatorModule.Result));
            SetLeftToRightCommand = new RelayCommand(_ => SetDirection(LineScanDirection.LeftToRight));
            SetTopToBottomCommand = new RelayCommand(_ => SetDirection(LineScanDirection.TopToBottom));
            SetBottomToTopCommand = new RelayCommand(_ => SetDirection(LineScanDirection.BottomToTop));
            SetRightToLeftCommand = new RelayCommand(_ => SetDirection(LineScanDirection.RightToLeft));
            SaveImageCommand = new RelayCommand(_ => SaveImageRequested?.Invoke());

            SetResult(null);
        }

        /// <summary>
        /// 当属性值发生变化时触发。
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 请求打开图像文件时触发。
        /// </summary>
        public event Action OpenImageRequested;

        /// <summary>
        /// 请求保存当前图像时触发。
        /// </summary>
        public event Action SaveImageRequested;

        /// <summary>
        /// 请求创建ROI时触发。
        /// </summary>
        public event Action<RoiShape> CreateRoiRequested;

        /// <summary>
        /// 请求清空ROI时触发。
        /// </summary>
        public event Action ClearRoiRequested;

        /// <summary>
        /// 请求刷新检测预览时触发。
        /// </summary>
        public event Action PreviewRequested;

        /// <summary>
        /// 请求执行直线检测时触发。
        /// </summary>
        public event Action DetectRequested;

        /// <summary>
        /// 请求回写ROI几何参数时触发。
        /// </summary>
        public event Action RoiGeometryChanged;

        /// <summary>
        /// 获取边缘极性选项。
        /// </summary>
        public ComboOption<LineEdgePolarity>[] PolarityOptions { get; private set; }

        /// <summary>
        /// 获取边缘选择选项。
        /// </summary>
        public ComboOption<LineSelectionMode>[] SelectionModeOptions { get; private set; }

        /// <summary>
        /// 获取拟合方式选项。
        /// </summary>
        public ComboOption<LineFitMode>[] FitModeOptions { get; private set; }

        /// <summary>
        /// 获取打开图像命令。
        /// </summary>
        public ICommand OpenImageCommand { get; private set; }

        /// <summary>
        /// 获取保存当前图像命令。
        /// </summary>
        public ICommand SaveImageCommand { get; private set; }

        /// <summary>
        /// 获取创建矩形ROI命令。
        /// </summary>
        public ICommand CreateRectangleRoiCommand { get; private set; }

        /// <summary>
        /// 获取创建旋转矩形ROI命令。
        /// </summary>
        public ICommand CreateRotatedRectangleRoiCommand { get; private set; }

        /// <summary>
        /// 获取清空ROI命令。
        /// </summary>
        public ICommand ClearRoiCommand { get; private set; }

        /// <summary>
        /// 获取切换到输入模块命令。
        /// </summary>
        public ICommand ShowInputCommand { get; private set; }

        /// <summary>
        /// 获取切换到ROI模块命令。
        /// </summary>
        public ICommand ShowRoiCommand { get; private set; }

        /// <summary>
        /// 获取切换到参数模块命令。
        /// </summary>
        public ICommand ShowParamsCommand { get; private set; }

        /// <summary>
        /// 获取切换到结果模块命令。
        /// </summary>
        public ICommand ShowResultCommand { get; private set; }

        /// <summary>
        /// 获取从左到右扫描命令。
        /// </summary>
        public ICommand SetLeftToRightCommand { get; private set; }

        /// <summary>
        /// 获取从上到下扫描命令。
        /// </summary>
        public ICommand SetTopToBottomCommand { get; private set; }

        /// <summary>
        /// 获取从下到上扫描命令。
        /// </summary>
        public ICommand SetBottomToTopCommand { get; private set; }

        /// <summary>
        /// 获取从右到左扫描命令。
        /// </summary>
        public ICommand SetRightToLeftCommand { get; private set; }

        /// <summary>
        /// 获取或设置当前模块。
        /// </summary>
        public OperatorModule CurrentModule
        {
            get { return currentModule; }
            private set
            {
                if (!SetProperty(ref currentModule, value)) return;
                OnPropertyChanged(nameof(IsInputModule));
                OnPropertyChanged(nameof(IsRoiModule));
                OnPropertyChanged(nameof(IsParamsModule));
                OnPropertyChanged(nameof(IsResultModule));
            }
        }

        /// <summary>
        /// 获取当前是否为输入模块。
        /// </summary>
        public bool IsInputModule => CurrentModule == OperatorModule.Input;

        /// <summary>
        /// 获取当前是否为ROI模块。
        /// </summary>
        public bool IsRoiModule => CurrentModule == OperatorModule.Roi;

        /// <summary>
        /// 获取当前是否为参数模块。
        /// </summary>
        public bool IsParamsModule => CurrentModule == OperatorModule.Params;

        /// <summary>
        /// 获取当前是否为结果模块。
        /// </summary>
        public bool IsResultModule => CurrentModule == OperatorModule.Result;

        /// <summary>
        /// 获取或设置当前扫描方向。
        /// </summary>
        public LineScanDirection CurrentDirection
        {
            get { return currentDirection; }
            private set
            {
                if (!SetProperty(ref currentDirection, value)) return;
                OnPropertyChanged(nameof(IsLeftToRight));
                OnPropertyChanged(nameof(IsTopToBottom));
                OnPropertyChanged(nameof(IsBottomToTop));
                OnPropertyChanged(nameof(IsRightToLeft));
                RequestDetection();
            }
        }

        /// <summary>
        /// 获取当前是否从左到右扫描。
        /// </summary>
        public bool IsLeftToRight => CurrentDirection == LineScanDirection.LeftToRight;

        /// <summary>
        /// 获取当前是否从上到下扫描。
        /// </summary>
        public bool IsTopToBottom => CurrentDirection == LineScanDirection.TopToBottom;

        /// <summary>
        /// 获取当前是否从下到上扫描。
        /// </summary>
        public bool IsBottomToTop => CurrentDirection == LineScanDirection.BottomToTop;

        /// <summary>
        /// 获取当前是否从右到左扫描。
        /// </summary>
        public bool IsRightToLeft => CurrentDirection == LineScanDirection.RightToLeft;

        /// <summary>
        /// 获取或设置选中的边缘极性。
        /// </summary>
        public ComboOption<LineEdgePolarity> SelectedPolarity
        {
            get { return selectedPolarity; }
            set
            {
                if (SetProperty(ref selectedPolarity, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置选中的边缘选择方式。
        /// </summary>
        public ComboOption<LineSelectionMode> SelectedSelectionMode
        {
            get { return selectedSelectionMode; }
            set
            {
                if (SetProperty(ref selectedSelectionMode, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置选中的拟合方式。
        /// </summary>
        public ComboOption<LineFitMode> SelectedFitMode
        {
            get { return selectedFitMode; }
            set
            {
                if (SetProperty(ref selectedFitMode, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置边缘阈值。
        /// </summary>
        public double Threshold
        {
            get { return threshold; }
            set
            {
                if (SetProperty(ref threshold, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置检测点个数。
        /// </summary>
        public double SampleCount
        {
            get { return sampleCount; }
            set
            {
                if (SetProperty(ref sampleCount, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置边缘宽度。
        /// </summary>
        public double EdgeWidth
        {
            get { return edgeWidth; }
            set
            {
                if (SetProperty(ref edgeWidth, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置投影宽度。
        /// </summary>
        public double ProjectionWidth
        {
            get { return projectionWidth; }
            set
            {
                if (SetProperty(ref projectionWidth, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置剔除比例。
        /// </summary>
        public double RejectRatio
        {
            get { return rejectRatio; }
            set
            {
                if (SetProperty(ref rejectRatio, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置剔除距离。
        /// </summary>
        public double RejectDistance
        {
            get { return rejectDistance; }
            set
            {
                if (SetProperty(ref rejectDistance, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置剖面图搜索线编号。
        /// </summary>
        public double ProfileLineIndex
        {
            get { return profileLineIndex; }
            set
            {
                if (SetProperty(ref profileLineIndex, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取或设置是否显示搜索线。
        /// </summary>
        public bool ShowSearchLines
        {
            get { return showSearchLines; }
            set
            {
                if (SetProperty(ref showSearchLines, value)) RequestDetection();
            }
        }

        /// <summary>
        /// 获取当前是否有可编辑ROI。
        /// </summary>
        public bool HasRoiGeometry
        {
            get { return hasRoiGeometry; }
            private set { SetProperty(ref hasRoiGeometry, value); }
        }

        /// <summary>
        /// 获取或设置ROI中心点X。
        /// </summary>
        public double RoiCenterX
        {
            get { return roiCenterX; }
            set
            {
                if (SetProperty(ref roiCenterX, value)) RequestRoiGeometryChange();
            }
        }

        /// <summary>
        /// 获取或设置ROI中心点Y。
        /// </summary>
        public double RoiCenterY
        {
            get { return roiCenterY; }
            set
            {
                if (SetProperty(ref roiCenterY, value)) RequestRoiGeometryChange();
            }
        }

        /// <summary>
        /// 获取或设置ROI宽度。
        /// </summary>
        public double RoiWidth
        {
            get { return roiWidth; }
            set
            {
                if (SetProperty(ref roiWidth, value)) RequestRoiGeometryChange();
            }
        }

        /// <summary>
        /// 获取或设置ROI高度。
        /// </summary>
        public double RoiHeight
        {
            get { return roiHeight; }
            set
            {
                if (SetProperty(ref roiHeight, value)) RequestRoiGeometryChange();
            }
        }

        /// <summary>
        /// 获取或设置ROI角度。
        /// </summary>
        public double RoiAngle
        {
            get { return roiAngle; }
            set
            {
                if (SetProperty(ref roiAngle, value)) RequestRoiGeometryChange();
            }
        }

        /// <summary>
        /// 获取或设置图像状态文本。
        /// </summary>
        public string ImageStatus
        {
            get { return imageStatus; }
            set { SetProperty(ref imageStatus, value); }
        }

        /// <summary>
        /// 获取或设置ROI状态文本。
        /// </summary>
        public string RoiStatus
        {
            get { return roiStatus; }
            set { SetProperty(ref roiStatus, value); }
        }

        /// <summary>
        /// 获取或设置实时状态文本。
        /// </summary>
        public string LiveStatus
        {
            get { return liveStatus; }
            private set { SetProperty(ref liveStatus, value); }
        }

        /// <summary>
        /// 获取或设置实时结果文本。
        /// </summary>
        public string LiveLine
        {
            get { return liveLine; }
            private set { SetProperty(ref liveLine, value); }
        }

        /// <summary>
        /// 获取或设置结果状态文本。
        /// </summary>
        public string ResultStatus
        {
            get { return resultStatus; }
            private set { SetProperty(ref resultStatus, value); }
        }

        /// <summary>
        /// 获取或设置结果起点文本。
        /// </summary>
        public string ResultStart
        {
            get { return resultStart; }
            private set { SetProperty(ref resultStart, value); }
        }

        /// <summary>
        /// 获取或设置结果终点文本。
        /// </summary>
        public string ResultEnd
        {
            get { return resultEnd; }
            private set { SetProperty(ref resultEnd, value); }
        }

        /// <summary>
        /// 获取或设置结果角度文本。
        /// </summary>
        public string ResultAngle
        {
            get { return resultAngle; }
            private set { SetProperty(ref resultAngle, value); }
        }

        /// <summary>
        /// 获取或设置检测点数量文本。
        /// </summary>
        public string ResultPointCount
        {
            get { return resultPointCount; }
            private set { SetProperty(ref resultPointCount, value); }
        }

        /// <summary>
        /// 获取或设置平均强度文本。
        /// </summary>
        public string ResultAverageStrength
        {
            get { return resultAverageStrength; }
            private set { SetProperty(ref resultAverageStrength, value); }
        }

        /// <summary>
        /// 获取或设置最大强度文本。
        /// </summary>
        public string ResultMaxStrength
        {
            get { return resultMaxStrength; }
            private set { SetProperty(ref resultMaxStrength, value); }
        }

        /// <summary>
        /// 获取或设置检测方向文本。
        /// </summary>
        public string ResultDirection
        {
            get { return resultDirection; }
            private set { SetProperty(ref resultDirection, value); }
        }

        /// <summary>
        /// 获取或设置检测耗时文本。
        /// </summary>
        public string ResultElapsed
        {
            get { return resultElapsed; }
            private set { SetProperty(ref resultElapsed, value); }
        }

        /// <summary>
        /// 获取或设置实时状态文字颜色。
        /// </summary>
        public Brush LiveStatusBrush
        {
            get { return liveStatusBrush; }
            private set { SetProperty(ref liveStatusBrush, value); }
        }

        /// <summary>
        /// 获取或设置结果状态文字颜色。
        /// </summary>
        public Brush ResultStatusBrush
        {
            get { return resultStatusBrush; }
            private set { SetProperty(ref resultStatusBrush, value); }
        }

        /// <summary>
        /// 创建当前直线检测参数。
        /// </summary>
        /// <returns>直线检测参数。</returns>
        public LineDetectionParams CreateParams()
        {
            return new LineDetectionParams
            {
                EdgeThreshold = (float)Threshold,
                SampleCount = Math.Max(2, (int)Math.Round(SampleCount)),
                EdgeWidth = Math.Max(1, (int)Math.Round(EdgeWidth)),
                ProjectionWidth = Math.Max(1, (int)Math.Round(ProjectionWidth)),
                RejectRatio = Math.Min(99, Math.Max(0, (int)Math.Round(RejectRatio))),
                RejectDistance = Math.Min(20, Math.Max(0, (int)Math.Round(RejectDistance))),
                ProfileLineIndex = Math.Max(1, (int)Math.Round(ProfileLineIndex)),
                ShowSearchLines = ShowSearchLines,
                EdgePolarity = SelectedPolarity == null ? LineEdgePolarity.Any : SelectedPolarity.Value,
                SelectionMode = SelectedSelectionMode == null ? LineSelectionMode.Strongest : SelectedSelectionMode.Value,
                FitMode = SelectedFitMode == null ? LineFitMode.Local : SelectedFitMode.Value,
                ScanDirection = CurrentDirection
            };
        }

        /// <summary>
        /// 按当前ROI刷新界面上的几何参数。
        /// </summary>
        /// <param name="roi">当前ROI。</param>
        public void SetRoiGeometry(RoiItem roi)
        {
            updatingRoiGeometry = true;
            try
            {
                HasRoiGeometry = roi != null;
                RoiCenterX = roi == null ? 0d : roi.Center.X;
                RoiCenterY = roi == null ? 0d : roi.Center.Y;
                RoiWidth = roi == null ? 0d : roi.Width;
                RoiHeight = roi == null ? 0d : roi.Height;
                RoiAngle = roi == null ? 0d : roi.Angle;
            }
            finally
            {
                updatingRoiGeometry = false;
            }
        }

        /// <summary>
        /// 根据检测结果更新界面文本。
        /// </summary>
        /// <param name="result">检测结果，为null时显示未检测。</param>
        public void SetResult(LineDetectionResult result)
        {
            if (result == null)
            {
                LiveStatus = "未检测";
                LiveLine = "-";
                ResultStatus = "未检测";
                ResultStart = "起点: -";
                ResultEnd = "终点: -";
                ResultAngle = "角度: -";
                ResultPointCount = "检测点数: -";
                ResultAverageStrength = "平均边缘强度: -";
                ResultMaxStrength = "最大边缘强度: -";
                ResultDirection = "检测方向: -";
                ResultElapsed = "检测耗时: -";
                LiveStatusBrush = Brushes.Red;
                ResultStatusBrush = Brushes.Red;
                return;
            }

            LiveStatus = result.Message;
            LiveStatusBrush = result.Success ? Brushes.ForestGreen : Brushes.Red;
            ResultStatus = result.Message;
            ResultStatusBrush = result.Success ? Brushes.ForestGreen : Brushes.Red;
            LiveLine = result.Success
                ? string.Format(CultureInfo.InvariantCulture, "起点 {0}，终点 {1}，角度 {2:F2}°，耗时 {3:F3} ms", FormatPoint(result.LineStart), FormatPoint(result.LineEnd), result.Angle, result.Elapsed.TotalMilliseconds)
                : string.Format(CultureInfo.InvariantCulture, "检测点数 {0}，耗时 {1:F3} ms", result.EdgePoints.Count, result.Elapsed.TotalMilliseconds);
            ResultStart = "起点: " + (result.Success ? FormatPoint(result.LineStart) : "-");
            ResultEnd = "终点: " + (result.Success ? FormatPoint(result.LineEnd) : "-");
            ResultAngle = "角度: " + (result.Success ? result.Angle.ToString("F2", CultureInfo.InvariantCulture) + "°" : "-");
            ResultPointCount = "检测点数: " + result.EdgePoints.Count.ToString(CultureInfo.InvariantCulture);
            ResultAverageStrength = "平均边缘强度: " + result.AverageStrength.ToString("F2", CultureInfo.InvariantCulture);
            ResultMaxStrength = "最大边缘强度: " + result.MaxStrength.ToString("F2", CultureInfo.InvariantCulture);
            ResultDirection = "检测方向: " + GetDirectionText(result.ScanDirection);
            ResultElapsed = "检测耗时: " + result.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";
        }

        /// <summary>
        /// 切换当前功能模块。
        /// </summary>
        /// <param name="module">目标模块。</param>
        private void SetModule(OperatorModule module)
        {
            CurrentModule = module;
            if (module == OperatorModule.Params)
            {
                RequestDetection();
            }
            else
            {
                PreviewRequested?.Invoke();
            }
        }

        /// <summary>
        /// 设置扫描方向。
        /// </summary>
        /// <param name="direction">扫描方向。</param>
        private void SetDirection(LineScanDirection direction)
        {
            CurrentDirection = direction;
        }

        /// <summary>
        /// 请求执行检测。
        /// </summary>
        private void RequestDetection()
        {
            DetectRequested?.Invoke();
        }

        /// <summary>
        /// 请求回写ROI几何参数。
        /// </summary>
        private void RequestRoiGeometryChange()
        {
            if (updatingRoiGeometry) return;
            RoiGeometryChanged?.Invoke();
        }

        /// <summary>
        /// 设置属性值并触发属性变化通知。
        /// </summary>
        /// <typeparam name="T">属性类型。</typeparam>
        /// <param name="field">属性字段。</param>
        /// <param name="value">新值。</param>
        /// <param name="propertyName">属性名称。</param>
        /// <returns>属性值变化时返回true。</returns>
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 触发属性变化通知。
        /// </summary>
        /// <param name="propertyName">属性名称。</param>
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 格式化点坐标。
        /// </summary>
        /// <param name="point">点坐标。</param>
        /// <returns>格式化后的坐标文本。</returns>
        private static string FormatPoint(PointF point)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", point.X, point.Y);
        }

        /// <summary>
        /// 获取扫描方向显示文本。
        /// </summary>
        /// <param name="direction">扫描方向。</param>
        /// <returns>方向文本。</returns>
        private static string GetDirectionText(LineScanDirection direction)
        {
            switch (direction)
            {
                case LineScanDirection.RightToLeft:
                    return "从右到左";
                case LineScanDirection.TopToBottom:
                    return "从上到下";
                case LineScanDirection.BottomToTop:
                    return "从下到上";
                default:
                    return "从左到右";
            }
        }
    }
}
