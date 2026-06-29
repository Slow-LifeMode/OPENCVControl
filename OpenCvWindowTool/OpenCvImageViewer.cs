using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenCvWindowTool
{
    /// <summary>
    /// OpenCV 图像显示控件，负责工具栏、状态栏和行为层的组合。
    /// </summary>
    public sealed class OpenCvImageViewer : UserControl
    {
        private static readonly Color ViewerBackgroundColor = Color.FromArgb(208, 208, 208);
        private static readonly Color StatusBarBackgroundColor = Color.FromArgb(96, 96, 96);

        private readonly ToolStrip toolStrip;
        private readonly Label statusLabel;
        private readonly BufferedImagePanel canvas;
        private readonly OpenCvViewerAction viewerAction;
        private readonly ToolTip hoverToolTip;
        private readonly Font hoverToolTipFont;
        private string hoverToolTipText;
        private ToolStripButton crosshairButton;

        /// <summary>
        /// 初始化 OpenCV 图像显示控件。
        /// </summary>
        public OpenCvImageViewer()
        {
            DoubleBuffered = true;
            BackColor = ViewerBackgroundColor;

            hoverToolTipFont = new Font("Microsoft YaHei UI", 8f);
            hoverToolTip = CreateHoverToolTip();
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
        /// 获取或设置是否显示状态栏。
        /// </summary>
        [Category("外观")]
        [Description("是否显示状态栏。")]
        public bool DisplayStatusBar
        {
            get { return statusLabel.Visible; }
            set { statusLabel.Visible = value; }
        }

        /// <summary>
        /// 获取或设置是否显示控件内置工具栏。
        /// </summary>
        [Category("外观")]
        [Description("是否显示控件内置工具栏。")]
        public bool DisplayToolBar
        {
            get { return toolStrip.Visible; }
            set { toolStrip.Visible = value; }
        }

        /// <summary>
        /// 获取当前 OpenCV 图像。
        /// </summary>
        [Browsable(false)]
        public Mat ImageMat => viewerAction.ImageMat;

        /// <summary>
        /// 获取用户叠加显示对象集合。
        /// </summary>
        [Browsable(false)]
        public IReadOnlyList<OverlayItem> Overlays => viewerAction.Overlays;

        /// <summary>
        /// 获取 ROI 对象集合。
        /// </summary>
        [Browsable(false)]
        public IReadOnlyList<RoiItem> Rois => viewerAction.Rois;

        /// <summary>
        /// 获取当前选中的 ROI。
        /// </summary>
        [Browsable(false)]
        public RoiItem SelectedRoi => viewerAction.SelectedRoi;

        /// <summary>
        /// 获取或设置是否显示图像。
        /// </summary>
        [Browsable(false)]
        public bool ShowImage
        {
            get { return viewerAction.ShowImage; }
            set { viewerAction.ShowImage = value; }
        }

        /// <summary>
        /// 获取或设置是否显示 ROI。
        /// </summary>
        [Browsable(false)]
        public bool ShowRois
        {
            get { return viewerAction.ShowRois; }
            set { viewerAction.ShowRois = value; }
        }

        /// <summary>
        /// 获取或设置是否允许 ROI 交互。
        /// </summary>
        [Browsable(false)]
        public bool EnableRoiInteraction
        {
            get { return viewerAction.EnableRoiInteraction; }
            set { viewerAction.EnableRoiInteraction = value; }
        }

        /// <summary>
        /// 获取或设置是否显示十字线。
        /// </summary>
        [Browsable(false)]
        public bool ShowCrosshair
        {
            get { return viewerAction.ShowCrosshair; }
            set
            {
                viewerAction.ShowCrosshair = value;
                if (crosshairButton != null && crosshairButton.Checked != value)
                {
                    crosshairButton.Checked = value;
                }
            }
        }

        /// <summary>
        /// ROI 选中对象变化事件。
        /// </summary>
        public event EventHandler SelectedRoiChanged;

        /// <summary>
        /// ROI 数据变化事件。
        /// </summary>
        public event EventHandler<RoiEventArgs> RoiChanged;

        /// <summary>
        /// ROI 编辑完成事件。
        /// </summary>
        public event EventHandler<RoiEventArgs> RoiEditCompleted;

        /// <summary>
        /// 进入创建 ROI 模式。
        /// </summary>
        /// <param name="shape">目标 ROI 类型。</param>
        public void StartCreateRoi(RoiShape shape)
        {
            viewerAction.StartCreateRoi(shape);
        }

        /// <summary>
        /// 加载图像文件。
        /// </summary>
        /// <param name="fileName">图像文件路径。</param>
        public void LoadImage(string fileName)
        {
            viewerAction.LoadImage(fileName);
        }

        /// <summary>
        /// 设置当前显示图像。
        /// </summary>
        /// <param name="mat">OpenCV 图像。</param>
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
        /// 按控件大小自适应图像显示。
        /// </summary>
        public void FitImage()
        {
            viewerAction.FitImage();
        }

        /// <summary>
        /// 放大图像。
        /// </summary>
        public void ZoomIn()
        {
            viewerAction.ZoomIn();
        }

        /// <summary>
        /// 缩小图像。
        /// </summary>
        public void ZoomOut()
        {
            viewerAction.ZoomOut();
        }

        /// <summary>
        /// 添加一个叠加显示对象。
        /// </summary>
        /// <param name="item">叠加对象。</param>
        public void AddOverlay(OverlayItem item)
        {
            viewerAction.AddOverlay(item);
        }

        /// <summary>
        /// 批量添加叠加显示对象。
        /// </summary>
        /// <param name="items">叠加对象集合。</param>
        /// <param name="cover">是否覆盖原有对象。</param>
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
        /// 添加一个 ROI。
        /// </summary>
        /// <param name="roi">ROI 对象。</param>
        public void AddRoi(RoiItem roi)
        {
            viewerAction.AddRoi(roi);
        }

        /// <summary>
        /// 批量添加 ROI。
        /// </summary>
        /// <param name="items">ROI 对象集合。</param>
        /// <param name="cover">是否覆盖原有 ROI。</param>
        public void AddRois(IEnumerable<RoiItem> items, bool cover = true)
        {
            viewerAction.AddRois(items, cover);
        }

        /// <summary>
        /// 删除指定 ROI。
        /// </summary>
        /// <param name="roi">目标 ROI。</param>
        public void DeleteRoi(RoiItem roi)
        {
            viewerAction.DeleteRoi(roi);
        }

        /// <summary>
        /// 清空全部 ROI。
        /// </summary>
        public void ClearRois()
        {
            viewerAction.ClearRois();
        }

        /// <summary>
        /// 执行直线检测并返回结果。
        /// </summary>
        /// <param name="roi">检测 ROI。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>直线检测结果。</returns>
        public LineDetectionResult DetectLine(RoiItem roi, LineDetectionParams parameters)
        {
            return viewerAction.DetectLine(roi, parameters);
        }

        /// <summary>
        /// 按指定模式执行直线检测并返回结果。
        /// </summary>
        /// <param name="roi">检测ROI。</param>
        /// <param name="parameters">检测参数。</param>
        /// <param name="mode">检测模式。</param>
        /// <returns>直线检测结果。</returns>
        public LineDetectionResult DetectLine(RoiItem roi, LineDetectionParams parameters, LineDetectionMode mode)
        {
            return viewerAction.DetectLine(roi, parameters, mode);
        }

        /// <summary>
        /// 显示直线检测结果。
        /// </summary>
        /// <param name="result">检测结果。</param>
        public void ShowLineDetectionResult(LineDetectionResult result)
        {
            viewerAction.ShowLineDetectionResult(result);
        }

        /// <summary>
        /// 按参数显示直线检测结果。
        /// </summary>
        /// <param name="result">检测结果。</param>
        /// <param name="parameters">检测参数。</param>
        public void ShowLineDetectionResult(LineDetectionResult result, LineDetectionParams parameters)
        {
            viewerAction.ShowLineDetectionResult(result, parameters);
        }

        /// <summary>
        /// 显示直线检测预览。
        /// </summary>
        /// <param name="frame">预览帧。</param>
        /// <param name="parameters">检测参数。</param>
        public void ShowLineDetectionPreview(LineDetectionFrame frame, LineDetectionParams parameters)
        {
            viewerAction.ShowLineDetectionPreview(frame, parameters);
        }

        /// <summary>
        /// 清空直线检测预览。
        /// </summary>
        public void ClearLineDetectionPreview()
        {
            viewerAction.ClearLineDetectionPreview();
        }

        /// <summary>
        /// 清空直线检测结果。
        /// </summary>
        public void ClearLineDetectionResult()
        {
            viewerAction.ClearLineDetectionResult();
        }

        /// <summary>
        /// 保存原始图像。
        /// </summary>
        /// <param name="fileName">保存路径。</param>
        public void SaveImage(string fileName)
        {
            viewerAction.SaveImage(fileName);
        }

        /// <summary>
        /// 保存当前窗口截图。
        /// </summary>
        /// <param name="fileName">保存路径。</param>
        public void SaveScreenShot(string fileName)
        {
            viewerAction.SaveScreenShot(fileName);
        }

        /// <summary>
        /// 释放控件资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                viewerAction?.Dispose();
                hoverToolTip?.Dispose();
                hoverToolTipFont?.Dispose();
                toolStrip?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// 创建顶部工具栏。
        /// </summary>
        /// <returns>工具栏实例。</returns>
        private ToolStrip CreateToolStrip()
        {
            ToolStrip result = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(224, 224, 224),
                ForeColor = Color.Black,
                ImageScalingSize = new System.Drawing.Size(16, 16),
                ShowItemToolTips = false,
                Padding = new Padding(2, 1, 2, 1)
            };

            result.Items.Add(CreateToolStripButton("放大", CreateZoomIcon(true), (s, e) => ZoomIn()));
            result.Items.Add(CreateToolStripButton("缩小", CreateZoomIcon(false), (s, e) => ZoomOut()));
            result.Items.Add(CreateToolStripButton("自适应", CreateFitIcon(), (s, e) => FitImage()));

            crosshairButton = CreateToolStripButton("十字线", CreateCrosshairIcon(), (s, e) => ShowCrosshair = crosshairButton.Checked, true);
            crosshairButton.Checked = false;
            result.Items.Add(crosshairButton);

            result.Items.Add(new ToolStripSeparator());
            result.Items.Add("清除ROI", null, (s, e) => ClearRois());
            result.Items.Add("创建矩形ROI", null, (s, e) => StartCreateRoi(RoiShape.Rectangle));
            result.Items.Add("创建带角度矩形ROI", null, (s, e) => StartCreateRoi(RoiShape.RotatedRectangle));
            result.Items.Add("创建圆环ROI", null, (s, e) => StartCreateRoi(RoiShape.Ring));
            result.Items.Add("保存原始图像", null, (s, e) => SaveWithDialog(false));
            result.Items.Add("保存窗口截图", null, (s, e) => SaveWithDialog(true));
            return result;
        }

        /// <summary>
        /// 创建图像显示画布。
        /// </summary>
        /// <returns>画布实例。</returns>
        private static BufferedImagePanel CreateCanvas()
        {
            return new BufferedImagePanel
            {
                Dock = DockStyle.Fill,
                BackColor = ViewerBackgroundColor
            };
        }

        /// <summary>
        /// 创建底部状态栏。
        /// </summary>
        /// <returns>状态栏标签。</returns>
        private static Label CreateStatusLabel()
        {
            return new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 28,
                BackColor = StatusBarBackgroundColor,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9f),
                Padding = new Padding(8, 0, 8, 0),
                AutoEllipsis = true,
                Text = "图像: -"
            };
        }

        /// <summary>
        /// 打开保存对话框并保存图像或截图。
        /// </summary>
        /// <param name="screenshot">是否保存窗口截图。</param>
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

        /// <summary>
        /// 创建放大镜图标。
        /// </summary>
        /// <param name="zoomIn">是否为放大图标。</param>
        /// <returns>图标位图。</returns>
        private static Bitmap CreateZoomIcon(bool zoomIn)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (Pen pen = new Pen(Color.FromArgb(54, 54, 54), 1.6f))
                using (Brush brush = new SolidBrush(Color.White))
                {
                    graphics.FillEllipse(brush, 1, 1, 8, 8);
                    graphics.DrawEllipse(pen, 1, 1, 8, 8);
                    graphics.DrawLine(pen, 7, 7, 14, 14);
                    graphics.DrawLine(pen, 3, 5, 7, 5);
                    if (zoomIn)
                    {
                        graphics.DrawLine(pen, 5, 3, 5, 7);
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 创建自适应图标。
        /// </summary>
        /// <returns>图标位图。</returns>
        private static Bitmap CreateFitIcon()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (Pen pen = new Pen(Color.FromArgb(54, 54, 54), 1.4f))
                {
                    graphics.DrawRectangle(pen, 3, 3, 10, 10);
                    graphics.DrawLine(pen, 3, 6, 1, 6);
                    graphics.DrawLine(pen, 3, 6, 3, 4);
                    graphics.DrawLine(pen, 6, 3, 6, 1);
                    graphics.DrawLine(pen, 6, 3, 4, 3);

                    graphics.DrawLine(pen, 13, 6, 15, 6);
                    graphics.DrawLine(pen, 13, 6, 13, 4);
                    graphics.DrawLine(pen, 10, 3, 10, 1);
                    graphics.DrawLine(pen, 10, 3, 12, 3);

                    graphics.DrawLine(pen, 3, 10, 1, 10);
                    graphics.DrawLine(pen, 3, 10, 3, 12);
                    graphics.DrawLine(pen, 6, 13, 6, 15);
                    graphics.DrawLine(pen, 6, 13, 4, 13);

                    graphics.DrawLine(pen, 13, 10, 15, 10);
                    graphics.DrawLine(pen, 13, 10, 13, 12);
                    graphics.DrawLine(pen, 10, 13, 10, 15);
                    graphics.DrawLine(pen, 10, 13, 12, 13);
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 创建十字线图标。
        /// </summary>
        /// <returns>图标位图。</returns>
        private static Bitmap CreateCrosshairIcon()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (Pen framePen = new Pen(Color.FromArgb(180, 180, 180), 1f))
                using (Pen pen = new Pen(Color.Red, 1f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    graphics.DrawRectangle(framePen, 2, 2, 11, 11);
                    graphics.DrawLine(pen, 2, 8, 13, 8);
                    graphics.DrawLine(pen, 8, 2, 8, 13);
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 创建工具栏按钮。
        /// </summary>
        /// <param name="text">按钮文本。</param>
        /// <param name="image">按钮图标。</param>
        /// <param name="clickHandler">点击处理函数。</param>
        /// <param name="checkOnClick">是否允许按下保持状态。</param>
        /// <returns>工具栏按钮。</returns>
        private ToolStripButton CreateToolStripButton(string text, Image image, EventHandler clickHandler, bool checkOnClick = false)
        {
            ToolStripButton button = new ToolStripButton
            {
                Text = text,
                Image = image,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AutoSize = false,
                Size = new System.Drawing.Size(28, 26),
                Margin = new Padding(0, 0, 2, 0),
                Padding = new Padding(0),
                ToolTipText = text,
                CheckOnClick = checkOnClick
            };
            button.Click += clickHandler;
            button.MouseEnter += (s, e) => ShowHoverToolTip(button);
            button.MouseLeave += (s, e) => HideHoverToolTip();
            return button;
        }

        /// <summary>
        /// 创建鼠标悬浮提示。
        /// </summary>
        /// <returns>悬浮提示对象。</returns>
        private ToolTip CreateHoverToolTip()
        {
            ToolTip tip = new ToolTip
            {
                AutomaticDelay = 0,
                AutoPopDelay = 1500,
                InitialDelay = 0,
                ReshowDelay = 0,
                ShowAlways = true,
                UseAnimation = false,
                UseFading = false,
                OwnerDraw = true
            };
            tip.Popup += HoverToolTip_Popup;
            tip.Draw += HoverToolTip_Draw;
            return tip;
        }

        /// <summary>
        /// 显示指定按钮的悬浮提示。
        /// </summary>
        /// <param name="button">目标按钮。</param>
        private void ShowHoverToolTip(ToolStripButton button)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.Text)) return;

            hoverToolTipText = button.Text;
            hoverToolTip.Show(hoverToolTipText, toolStrip, new System.Drawing.Point(button.Bounds.Left, button.Bounds.Bottom + 2));
        }

        /// <summary>
        /// 隐藏悬浮提示。
        /// </summary>
        private void HideHoverToolTip()
        {
            hoverToolTipText = string.Empty;
            hoverToolTip.Hide(toolStrip);
        }

        /// <summary>
        /// 计算悬浮提示尺寸。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">弹窗参数。</param>
        private void HoverToolTip_Popup(object sender, PopupEventArgs e)
        {
            System.Drawing.Size textSize = TextRenderer.MeasureText(hoverToolTipText ?? string.Empty, hoverToolTipFont);
            e.ToolTipSize = new System.Drawing.Size(textSize.Width + 10, textSize.Height + 6);
        }

        /// <summary>
        /// 绘制悬浮提示内容。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">绘制参数。</param>
        private void HoverToolTip_Draw(object sender, DrawToolTipEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(252, 252, 252)))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            e.Graphics.DrawRectangle(Pens.Gray, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));
            TextRenderer.DrawText(
                e.Graphics,
                hoverToolTipText ?? string.Empty,
                hoverToolTipFont,
                e.Bounds,
                Color.FromArgb(48, 48, 48),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
