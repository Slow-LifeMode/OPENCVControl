using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace OpenCvWindowTool
{
    /// <summary>
    /// OpenCV图像显示控件外观层，负责控件组合、公开接口和外部事件转发。
    /// </summary>
    public sealed class OpenCvImageViewer : UserControl
    {
        private readonly ToolStrip toolStrip;
        private readonly Label statusLabel;
        private readonly BufferedImagePanel canvas;
        private readonly OpenCvViewerAction viewerAction;

        /// <summary>
        /// 初始化OpenCV图像显示控件。
        /// </summary>
        public OpenCvImageViewer()
        {
            DoubleBuffered = true;
            BackColor = System.Drawing.Color.Black;

            toolStrip = CreateToolStrip();
            canvas = CreateCanvas();
            statusLabel = CreateStatusLabel();
            viewerAction = new OpenCvViewerAction(this, canvas, statusLabel);
            viewerAction.SelectedRoiChanged += (s, e) => SelectedRoiChanged?.Invoke(this, e);
            viewerAction.RoiChanged += (s, e) => RoiChanged?.Invoke(this, e);
            viewerAction.RoiEditCompleted += (s, e) => RoiEditCompleted?.Invoke(this, e);

            Controls.Add(canvas);
            Controls.Add(statusLabel);
            Controls.Add(toolStrip);
        }

        /// <summary>
        /// 是否显示状态栏。
        /// </summary>
        [Category("外观")]
        [Description("是否显示状态栏。")]
        public bool DisplayStatusBar
        {
            get { return statusLabel.Visible; }
            set { statusLabel.Visible = value; }
        }

        /// <summary>
        /// 是否显示控件内置工具栏。
        /// </summary>
        [Category("外观")]
        [Description("是否显示控件内置工具栏。")]
        public bool DisplayToolBar
        {
            get { return toolStrip.Visible; }
            set { toolStrip.Visible = value; }
        }

        /// <summary>
        /// 当前OpenCV图像。
        /// </summary>
        [Browsable(false)]
        public Mat ImageMat => viewerAction.ImageMat;

        /// <summary>
        /// 用户叠加显示对象集合。
        /// </summary>
        [Browsable(false)]
        public IReadOnlyList<OverlayItem> Overlays => viewerAction.Overlays;

        /// <summary>
        /// ROI对象集合。
        /// </summary>
        [Browsable(false)]
        public IReadOnlyList<RoiItem> Rois => viewerAction.Rois;

        /// <summary>
        /// 当前选中的ROI。
        /// </summary>
        [Browsable(false)]
        public RoiItem SelectedRoi => viewerAction.SelectedRoi;

        [Browsable(false)]
        public bool ShowImage
        {
            get { return viewerAction.ShowImage; }
            set { viewerAction.ShowImage = value; }
        }

        [Browsable(false)]
        public bool ShowRois
        {
            get { return viewerAction.ShowRois; }
            set { viewerAction.ShowRois = value; }
        }

        [Browsable(false)]
        public bool EnableRoiInteraction
        {
            get { return viewerAction.EnableRoiInteraction; }
            set { viewerAction.EnableRoiInteraction = value; }
        }

        /// <summary>
        /// ROI选中对象变化事件。
        /// </summary>
        public event EventHandler SelectedRoiChanged;

        /// <summary>
        /// ROI数据变化事件。
        /// </summary>
        public event EventHandler<RoiEventArgs> RoiChanged;

        public event EventHandler<RoiEventArgs> RoiEditCompleted;

        /// <summary>
        /// 进入创建ROI模式，下一次鼠标拖拽会创建指定类型的ROI。
        /// </summary>
        public void StartCreateRoi(RoiShape shape)
        {
            viewerAction.StartCreateRoi(shape);
        }

        /// <summary>
        /// 加载图像文件。
        /// </summary>
        public void LoadImage(string fileName)
        {
            viewerAction.LoadImage(fileName);
        }

        /// <summary>
        /// 设置当前显示图像。
        /// </summary>
        public void SetImage(Mat mat)
        {
            viewerAction.SetImage(mat);
        }

        /// <summary>
        /// 清空图像和显示数据。
        /// </summary>
        public void ClearImage()
        {
            viewerAction.ClearImage();
        }

        /// <summary>
        /// 按控件大小适应图像显示。
        /// </summary>
        public void FitImage()
        {
            viewerAction.FitImage();
        }

        /// <summary>
        /// 添加一个叠加显示对象。
        /// </summary>
        public void AddOverlay(OverlayItem item)
        {
            viewerAction.AddOverlay(item);
        }

        /// <summary>
        /// 批量添加叠加显示对象。
        /// </summary>
        public void AddOverlays(IEnumerable<OverlayItem> items, bool cover = true)
        {
            viewerAction.AddOverlays(items, cover);
        }

        /// <summary>
        /// 清空叠加显示对象。
        /// </summary>
        public void ClearOverlays()
        {
            viewerAction.ClearOverlays();
        }

        /// <summary>
        /// 添加一个ROI。
        /// </summary>
        public void AddRoi(RoiItem roi)
        {
            viewerAction.AddRoi(roi);
        }

        /// <summary>
        /// 批量添加ROI。
        /// </summary>
        public void AddRois(IEnumerable<RoiItem> items, bool cover = true)
        {
            viewerAction.AddRois(items, cover);
        }

        /// <summary>
        /// 删除指定ROI。
        /// </summary>
        public void DeleteRoi(RoiItem roi)
        {
            viewerAction.DeleteRoi(roi);
        }

        /// <summary>
        /// 清空全部ROI。
        /// </summary>
        public void ClearRois()
        {
            viewerAction.ClearRois();
        }

        /// <summary>
        /// 执行直线检测并显示结果。
        /// </summary>
        public LineDetectionResult DetectLine(RoiItem roi, LineDetectionParams parameters)
        {
            return viewerAction.DetectLine(roi, parameters);
        }

        /// <summary>
        /// 显示直线检测结果。
        /// </summary>
        public void ShowLineDetectionResult(LineDetectionResult result)
        {
            viewerAction.ShowLineDetectionResult(result);
        }

        public void ShowLineDetectionResult(LineDetectionResult result, LineDetectionParams parameters)
        {
            viewerAction.ShowLineDetectionResult(result, parameters);
        }

        public void ShowLineDetectionPreview(LineDetectionFrame frame, LineDetectionParams parameters)
        {
            viewerAction.ShowLineDetectionPreview(frame, parameters);
        }

        public void ClearLineDetectionPreview()
        {
            viewerAction.ClearLineDetectionPreview();
        }

        /// <summary>
        /// 清除直线检测结果。
        /// </summary>
        public void ClearLineDetectionResult()
        {
            viewerAction.ClearLineDetectionResult();
        }

        /// <summary>
        /// 保存原始图像。
        /// </summary>
        public void SaveImage(string fileName)
        {
            viewerAction.SaveImage(fileName);
        }

        /// <summary>
        /// 保存当前窗口截图。
        /// </summary>
        public void SaveScreenShot(string fileName)
        {
            viewerAction.SaveScreenShot(fileName);
        }

        /// <summary>
        /// 释放控件资源。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                viewerAction?.Dispose();
                toolStrip?.Dispose();
            }
            base.Dispose(disposing);
        }

        private ToolStrip CreateToolStrip()
        {
            ToolStrip result = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            result.Items.Add("适应图像显示", null, (s, e) => FitImage());
            result.Items.Add("清除ROI", null, (s, e) => ClearRois());
            result.Items.Add("创建矩形ROI", null, (s, e) => StartCreateRoi(RoiShape.Rectangle));
            result.Items.Add("创建带角度矩形ROI", null, (s, e) => StartCreateRoi(RoiShape.RotatedRectangle));
            result.Items.Add("创建圆环ROI", null, (s, e) => StartCreateRoi(RoiShape.Ring));
            result.Items.Add("保存原始图像", null, (s, e) => SaveWithDialog(false));
            result.Items.Add("保存窗口截图", null, (s, e) => SaveWithDialog(true));
            return result;
        }

        private static BufferedImagePanel CreateCanvas()
        {
            return new BufferedImagePanel { Dock = DockStyle.Fill, BackColor = System.Drawing.Color.Black };
        }

        private static Label CreateStatusLabel()
        {
            return new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 24,
                BackColor = System.Drawing.Color.FromArgb(180, 0, 0, 0),
                ForeColor = System.Drawing.Color.White,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9f)
            };
        }

        private void SaveWithDialog(bool screenshot)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG 图像|*.png|BMP 图像|*.bmp|JPG 图像|*.jpg;*.jpeg|所有文件|*.*";
                dialog.FileName = screenshot ? "窗口截图.png" : "原始图像.png";
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (screenshot) SaveScreenShot(dialog.FileName);
                else SaveImage(dialog.FileName);
            }
        }
    }
}
