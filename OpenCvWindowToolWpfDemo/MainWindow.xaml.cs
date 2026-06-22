using Microsoft.Win32;
using OpenCvWindowTool;
using OpenCvWindowToolWpfDemo.ViewModels;
using System;
using System.Linq;
using System.Windows;

namespace OpenCvWindowToolWpfDemo
{
    /// <summary>
    /// 主窗口负责连接WPF视图模型和WinForms图像显示控件。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly OpenCvImageViewer viewer;
        private readonly MainWindowViewModel viewModel;
        private LineDetectionResult latestResult;
        private bool refreshingLineDisplay;

        /// <summary>
        /// 初始化主窗口。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            viewModel = new MainWindowViewModel();
            DataContext = viewModel;
            SubscribeViewModelEvents();

            viewer = new OpenCvImageViewer { DisplayToolBar = false };
            viewer.RoiChanged += Viewer_RoiChanged;
            viewer.RoiEditCompleted += Viewer_RoiEditCompleted;
            viewer.SelectedRoiChanged += Viewer_SelectedRoiChanged;
            FormsHost.Child = viewer;

            RefreshLineDisplay(false);
        }

        /// <summary>
        /// 订阅视图模型请求事件。
        /// </summary>
        private void SubscribeViewModelEvents()
        {
            viewModel.OpenImageRequested += OpenImage;
            viewModel.SaveImageRequested += SaveCurrentImage;
            viewModel.CreateRoiRequested += CreateRoi;
            viewModel.ClearRoiRequested += ClearRoi;
            viewModel.PreviewRequested += () => RefreshLineDisplay(false);
            viewModel.DetectRequested += () => RefreshLineDisplay(true);
        }

        /// <summary>
        /// 打开图像文件并刷新检测显示。
        /// </summary>
        private void OpenImage()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*"
            };

            if (dialog.ShowDialog(this) != true) return;

            viewer.LoadImage(dialog.FileName);
            viewModel.ImageStatus = dialog.FileName;
            latestResult = null;
            RefreshLineDisplay(false);
        }

        /// <summary>
        /// 进入指定ROI创建模式。
        /// </summary>
        /// <param name="shape">ROI形状。</param>
        /// <summary>
        /// 保存当前控件中显示的图像。
        /// </summary>
        private void SaveCurrentImage()
        {
            if (viewer.ImageMat == null || viewer.ImageMat.Empty())
            {
                MessageBox.Show(this, "当前没有可保存的图像。", "保存图像", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "PNG 图像|*.png|BMP 图像|*.bmp|JPG 图像|*.jpg;*.jpeg|TIFF 图像|*.tif;*.tiff|所有文件|*.*",
                FileName = "灰度图像.png"
            };

            if (dialog.ShowDialog(this) != true) return;
            viewer.SaveImage(dialog.FileName);
        }

        private void CreateRoi(RoiShape shape)
        {
            viewer.StartCreateRoi(shape);
            viewModel.RoiStatus = shape == RoiShape.RotatedRectangle
                ? "正在创建带角度矩形ROI"
                : "正在创建矩形检测ROI";
        }

        /// <summary>
        /// 清空ROI和检测结果。
        /// </summary>
        private void ClearRoi()
        {
            viewer.ClearRois();
            viewer.ClearLineDetectionPreview();
            viewer.ClearLineDetectionResult();
            latestResult = null;
            viewModel.RoiStatus = "未创建ROI";
            viewModel.SetResult(null);
        }

        /// <summary>
        /// 处理ROI几何变化事件，拖动中只刷新预览。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">ROI事件参数。</param>
        private void Viewer_RoiChanged(object sender, RoiEventArgs e)
        {
            viewModel.RoiStatus = viewer.SelectedRoi == null ? "未选择ROI" : viewer.SelectedRoi.Name;
            if (refreshingLineDisplay) return;
            RefreshLineDisplay(false);
        }

        /// <summary>
        /// 处理ROI编辑完成事件，鼠标释放后同步检测。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">ROI事件参数。</param>
        private void Viewer_RoiEditCompleted(object sender, RoiEventArgs e)
        {
            viewModel.RoiStatus = viewer.SelectedRoi == null ? "未选择ROI" : viewer.SelectedRoi.Name;
            RefreshLineDisplay(true);
        }

        /// <summary>
        /// 处理当前选中ROI变化事件。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void Viewer_SelectedRoiChanged(object sender, EventArgs e)
        {
            viewModel.RoiStatus = viewer.SelectedRoi == null ? "未选择ROI" : viewer.SelectedRoi.Name;
            RefreshLineDisplay(false);
        }

        /// <summary>
        /// 刷新检测预览和检测结果。
        /// </summary>
        /// <param name="runDetection">是否执行同步检测。</param>
        private void RefreshLineDisplay(bool runDetection)
        {
            if (viewer == null || refreshingLineDisplay) return;

            refreshingLineDisplay = true;
            try
            {
                LineDetectionParams parameters = viewModel.CreateParams();
                RoiItem roi = GetCurrentLineRoi();
                ResetRoiColor();

                if (roi == null)
                {
                    latestResult = null;
                    viewer.ClearLineDetectionPreview();
                    viewer.ClearLineDetectionResult();
                    viewModel.SetResult(null);
                    return;
                }

                roi.Color = System.Drawing.Color.DeepSkyBlue;
                viewer.ShowLineDetectionPreview(roi.ToLineDetectionFrame(), parameters);

                if (!runDetection)
                {
                    return;
                }

                latestResult = viewer.DetectLine(roi, parameters);
                roi.Color = latestResult.Success ? System.Drawing.Color.DeepSkyBlue : System.Drawing.Color.Red;
                viewer.ShowLineDetectionResult(latestResult, parameters);
                viewModel.SetResult(latestResult);
            }
            catch (Exception ex)
            {
                LineDetectionParams parameters = viewModel.CreateParams();
                RoiItem roi = GetCurrentLineRoi();
                LineDetectionFrame frame = roi == null ? default(LineDetectionFrame) : roi.ToLineDetectionFrame();
                latestResult = LineDetectionResult.CreateFailure("检测失败: " + ex.Message, frame, parameters.ScanDirection);
                if (roi != null) roi.Color = System.Drawing.Color.Red;
                viewer.ShowLineDetectionResult(latestResult, parameters);
                viewModel.SetResult(latestResult);
            }
            finally
            {
                refreshingLineDisplay = false;
            }
        }

        /// <summary>
        /// 将所有ROI颜色恢复为默认检测颜色。
        /// </summary>
        private void ResetRoiColor()
        {
            foreach (RoiItem roi in viewer.Rois)
            {
                roi.Color = System.Drawing.Color.DeepSkyBlue;
            }
        }

        /// <summary>
        /// 获取当前可用于直线检测的ROI。
        /// </summary>
        /// <returns>可检测ROI，没有时返回null。</returns>
        private RoiItem GetCurrentLineRoi()
        {
            return viewer.SelectedRoi != null && viewer.SelectedRoi.CanDetectLine()
                ? viewer.SelectedRoi
                : viewer.Rois.FirstOrDefault(x => x.CanDetectLine());
        }
    }
}
