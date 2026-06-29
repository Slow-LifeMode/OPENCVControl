using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 图像控件行为层，负责显示、缩放、平移、ROI交互、检测结果叠加和状态栏更新。
    /// </summary>
    internal sealed class OpenCvViewerAction : IDisposable
    {
        private readonly Control owner;
        private readonly BufferedImagePanel canvas;
        private readonly Label statusLabel;
        private readonly List<OverlayItem> overlays = new List<OverlayItem>();
        private readonly List<OverlayItem> lineDetectionOverlays = new List<OverlayItem>();
        private readonly List<OverlayItem> linePreviewOverlays = new List<OverlayItem>();
        private readonly List<RoiItem> rois = new List<RoiItem>();
        private readonly LineDetectionOperator lineDetectionOperator = new LineDetectionOperator();
        private readonly OptLineDetectionOperator optLineDetectionOperator = new OptLineDetectionOperator();
        private Mat image;
        private LineDetectionImageContext lineDetectionContext;
        private Bitmap bitmap;
        private float zoom = 1f;
        private PointF pan;
        private bool dragging;
        private System.Drawing.Point dragStart;
        private PointF dragPanStart;
        private PointF dragImageStart;
        private RoiItem selectedRoi;
        private RoiHitPart activeRoiPart;
        private RoiHit pendingRoiHit;
        private InteractionMode interactionMode;
        private RoiShape createRoiShape;
        private RoiItem creatingRoi;
        private bool roiGeometryChanged;
        private bool showImage = true;
        private bool showRois = true;
        private bool showCrosshair;
        private bool enableRoiInteraction = true;

        /// <summary>
        /// 缩放上限。
        /// </summary>
        private const float MaximumZoom = 1000f;

        /// <summary>
        /// 像素网格显示阈值，缩放超过该值后开始显示黑色分割线。
        /// </summary>
        private const float PixelGridZoomThreshold = 25f;

        /// <summary>
        /// 初始化行为层并绑定画布事件。
        /// </summary>
        public OpenCvViewerAction(Control owner, BufferedImagePanel canvas, Label statusLabel)
        {
            this.owner = owner;
            this.canvas = canvas;
            this.statusLabel = statusLabel;
            SubscribeCanvasEvents();
        }

        /// <summary>
        /// 当前OpenCV图像。
        /// </summary>
        public Mat ImageMat => image;

        /// <summary>
        /// 用户叠加显示对象集合。
        /// </summary>
        public IReadOnlyList<OverlayItem> Overlays => overlays;

        /// <summary>
        /// ROI对象集合。
        /// </summary>
        public IReadOnlyList<RoiItem> Rois => rois;

        /// <summary>
        /// 当前选中的ROI。
        /// </summary>
        public RoiItem SelectedRoi => selectedRoi;

        public bool ShowImage
        {
            get { return showImage; }
            set
            {
                if (showImage == value) return;
                showImage = value;
                canvas.Invalidate();
            }
        }

        public bool ShowRois
        {
            get { return showRois; }
            set
            {
                if (showRois == value) return;
                showRois = value;
                canvas.Invalidate();
            }
        }

        public bool EnableRoiInteraction
        {
            get { return enableRoiInteraction; }
            set
            {
                if (enableRoiInteraction == value) return;
                enableRoiInteraction = value;
                if (!enableRoiInteraction && interactionMode != InteractionMode.CreateRoiDragging)
                {
                    interactionMode = InteractionMode.None;
                    activeRoiPart = RoiHitPart.None;
                    pendingRoiHit = new RoiHit(null, RoiHitPart.None);
                    dragging = false;
                    canvas.Capture = false;
                    canvas.Cursor = Cursors.Default;
                    canvas.Invalidate();
                }
            }
        }

        /// <summary>
        /// 获取或设置是否显示红色十字线。
        /// </summary>
        public bool ShowCrosshair
        {
            get { return showCrosshair; }
            set
            {
                if (showCrosshair == value) return;
                showCrosshair = value;
                canvas.Invalidate();
            }
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
            createRoiShape = shape;
            creatingRoi = null;
            interactionMode = InteractionMode.CreateRoiReady;
            canvas.Cursor = Cursors.Cross;
            canvas.Focus();
        }

        /// <summary>
        /// 加载图像文件。
        /// </summary>
        public void LoadImage(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));
            using (Mat loaded = Cv2.ImRead(fileName, ImreadModes.Unchanged))
            {
                SetImage(loaded);
            }
        }

        /// <summary>
        /// 设置当前显示图像。
        /// </summary>
        public void SetImage(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                ClearImage();
                return;
            }

            using (Mat gray = LineDetectionImageContext.ToGray(mat))
            {
                image?.Dispose();
                image = gray.Clone();
            }

            lineDetectionContext?.Dispose();
            lineDetectionContext = new LineDetectionImageContext(image);
            bitmap?.Dispose();
            bitmap = OpenCvImageConverter.ToBitmap(image);
            ClearLineDetectionResult();
            FitImage();
            UpdateStatus(new System.Drawing.Point(-1, -1));
        }

        /// <summary>
        /// 清空图像和所有显示数据。
        /// </summary>
        public void ClearImage()
        {
            image?.Dispose();
            image = null;
            lineDetectionContext?.Dispose();
            lineDetectionContext = null;
            bitmap?.Dispose();
            bitmap = null;
            overlays.Clear();
            lineDetectionOverlays.Clear();
            linePreviewOverlays.Clear();
            UnsubscribeRoiRefreshDisplayEvent(rois);
            rois.Clear();
            SetSelectedRoi(null);
            statusLabel.Text = "图像: -";
            canvas.Invalidate();
        }

        /// <summary>
        /// 按画布大小自适应显示图像。
        /// </summary>
        public void FitImage()
        {
            if (bitmap == null || canvas.Width <= 0 || canvas.Height <= 0)
            {
                canvas.Invalidate();
                return;
            }

            float scaleX = (float)canvas.Width / bitmap.Width;
            float scaleY = (float)canvas.Height / bitmap.Height;
            zoom = Math.Max(0.01f, Math.Min(scaleX, scaleY));
            pan = new PointF((canvas.Width - bitmap.Width * zoom) / 2f, (canvas.Height - bitmap.Height * zoom) / 2f);
            canvas.Invalidate();
            UpdateStatus(new System.Drawing.Point(-1, -1));
        }

        /// <summary>
        /// 放大图像。
        /// </summary>
        public void ZoomIn()
        {
            ZoomAt(new System.Drawing.Point(canvas.Width / 2, canvas.Height / 2), 1.2f);
        }

        /// <summary>
        /// 缩小图像。
        /// </summary>
        public void ZoomOut()
        {
            ZoomAt(new System.Drawing.Point(canvas.Width / 2, canvas.Height / 2), 1f / 1.2f);
        }

        /// <summary>
        /// 添加一个用户叠加显示对象。
        /// </summary>
        public void AddOverlay(OverlayItem item)
        {
            if (item == null) return;
            overlays.Add(item);
            canvas.Invalidate();
        }

        /// <summary>
        /// 批量添加用户叠加显示对象。
        /// </summary>
        public void AddOverlays(IEnumerable<OverlayItem> items, bool cover)
        {
            if (items == null) return;
            if (cover) overlays.Clear();
            overlays.AddRange(items.Where(x => x != null));
            canvas.Invalidate();
        }

        /// <summary>
        /// 清空用户叠加显示对象。
        /// </summary>
        public void ClearOverlays()
        {
            overlays.Clear();
            canvas.Invalidate();
        }

        /// <summary>
        /// 添加一个ROI。
        /// </summary>
        public void AddRoi(RoiItem roi)
        {
            if (roi == null) return;
            UnsubscribeRoiRefreshDisplayEvent(rois);
            rois.Clear();
            ClearLineDetectionPreview();
            ClearLineDetectionResult();
            roi.RefreshDisplay -= Roi_RefreshDisplay;
            roi.RefreshDisplay += Roi_RefreshDisplay;
            rois.Add(roi);
            SetSelectedRoi(roi);
            canvas.Invalidate();
        }

        /// <summary>
        /// 批量添加ROI。
        /// </summary>
        public void AddRois(IEnumerable<RoiItem> items, bool cover)
        {
            if (items == null) return;
            UnsubscribeRoiRefreshDisplayEvent(rois);
            rois.Clear();
            selectedRoi = null;
            ClearLineDetectionPreview();
            ClearLineDetectionResult();

            foreach (RoiItem item in items.Where(x => x != null).Take(1))
            {
                item.RefreshDisplay -= Roi_RefreshDisplay;
                item.RefreshDisplay += Roi_RefreshDisplay;
                rois.Add(item);
                selectedRoi = item;
            }

            SelectedRoiChanged?.Invoke(owner, EventArgs.Empty);
            canvas.Invalidate();
        }

        /// <summary>
        /// 删除指定ROI。
        /// </summary>
        public void DeleteRoi(RoiItem roi)
        {
            if (roi == null) return;
            roi.RefreshDisplay -= Roi_RefreshDisplay;
            rois.Remove(roi);
            if (selectedRoi == roi) SetSelectedRoi(null);
            canvas.Invalidate();
        }

        /// <summary>
        /// 清空全部ROI。
        /// </summary>
        public void ClearRois()
        {
            UnsubscribeRoiRefreshDisplayEvent(rois);
            rois.Clear();
            SetSelectedRoi(null);
            ClearLineDetectionResult();
            canvas.Invalidate();
        }

        /// <summary>
        /// 执行直线检测。
        /// </summary>
        public LineDetectionResult DetectLine(RoiItem roi, LineDetectionParams parameters)
        {
            return DetectLine(roi, parameters, LineDetectionMode.SelfMode);
        }

        /// <summary>
        /// 按指定模式执行直线检测。
        /// </summary>
        public LineDetectionResult DetectLine(RoiItem roi, LineDetectionParams parameters, LineDetectionMode mode)
        {
            return mode == LineDetectionMode.OPTMode
                ? optLineDetectionOperator.Detect(lineDetectionContext, roi, parameters)
                : lineDetectionOperator.Detect(lineDetectionContext, roi, parameters);
        }

        /// <summary>
        /// 显示直线检测结果。
        /// </summary>
        public void ShowLineDetectionResult(LineDetectionResult result)
        {
            lineDetectionOverlays.Clear();
            lineDetectionOverlays.AddRange(LineDetectionOverlayBuilder.Build(result));
            canvas.Invalidate();
        }

        public void ShowLineDetectionResult(LineDetectionResult result, LineDetectionParams parameters)
        {
            lineDetectionOverlays.Clear();
            lineDetectionOverlays.AddRange(LineDetectionOverlayBuilder.Build(result, parameters));
            canvas.Invalidate();
        }

        public void ShowLineDetectionPreview(LineDetectionFrame frame, LineDetectionParams parameters)
        {
            linePreviewOverlays.Clear();
            linePreviewOverlays.AddRange(LineDetectionOverlayBuilder.BuildPreview(frame, parameters));
            canvas.Invalidate();
        }

        public void ClearLineDetectionPreview()
        {
            linePreviewOverlays.Clear();
            canvas.Invalidate();
        }

        /// <summary>
        /// 清除直线检测结果。
        /// </summary>
        public void ClearLineDetectionResult()
        {
            lineDetectionOverlays.Clear();
            canvas.Invalidate();
        }

        /// <summary>
        /// 保存原始图像。
        /// </summary>
        public void SaveImage(string fileName)
        {
            if (image == null || image.Empty()) return;
            Cv2.ImWrite(fileName, image);
        }

        /// <summary>
        /// 保存当前窗口截图。
        /// </summary>
        public void SaveScreenShot(string fileName)
        {
            using (Bitmap shot = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(shot, new Rectangle(System.Drawing.Point.Empty, canvas.Size));
                shot.Save(fileName);
            }
        }

        /// <summary>
        /// 释放图像资源并解除事件订阅。
        /// </summary>
        public void Dispose()
        {
            UnsubscribeCanvasEvents();
            UnsubscribeRoiRefreshDisplayEvent(rois);
            lineDetectionContext?.Dispose();
            image?.Dispose();
            bitmap?.Dispose();
        }

        private void SubscribeCanvasEvents()
        {
            canvas.Paint += Canvas_Paint;
            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseEnter += Canvas_MouseEnter;
            canvas.Resize += Canvas_Resize;
        }

        private void UnsubscribeCanvasEvents()
        {
            canvas.Paint -= Canvas_Paint;
            canvas.MouseWheel -= Canvas_MouseWheel;
            canvas.MouseDown -= Canvas_MouseDown;
            canvas.MouseMove -= Canvas_MouseMove;
            canvas.MouseUp -= Canvas_MouseUp;
            canvas.MouseEnter -= Canvas_MouseEnter;
            canvas.Resize -= Canvas_Resize;
        }

        private void Roi_RefreshDisplay()
        {
            canvas.Invalidate();
            if (selectedRoi != null && !IsRoiEditing)
            {
                RoiChanged?.Invoke(owner, new RoiEventArgs(selectedRoi));
            }
        }

        private void UnsubscribeRoiRefreshDisplayEvent(IEnumerable<RoiItem> roiItems)
        {
            foreach (RoiItem item in roiItems)
            {
                item.RefreshDisplay -= Roi_RefreshDisplay;
            }
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(canvas.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (bitmap == null || !showImage) return;

            PixelOffsetMode oldPixelOffsetMode = e.Graphics.PixelOffsetMode;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            bool isRoiEditing = IsRoiEditing;
            RectangleF imageRect = GetImageRect();
            e.Graphics.DrawImage(bitmap, imageRect);
            e.Graphics.PixelOffsetMode = oldPixelOffsetMode;
            if (zoom >= PixelGridZoomThreshold)
            {
                DrawPixelGrid(e.Graphics, imageRect);
            }
            if (showCrosshair)
            {
                DrawCrosshair(e.Graphics, imageRect);
            }
            if (!isRoiEditing)
            {
                DrawOverlays(e.Graphics, overlays);
            }
            if (showRois)
            {
                DrawRois(e.Graphics, true);
                if (!isRoiEditing)
                {
                    DrawOverlays(e.Graphics, linePreviewOverlays);
                    DrawOverlays(e.Graphics, lineDetectionOverlays);
                }
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (bitmap == null) return;

            PointF imagePoint = ToImage(e.Location);
            float factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;
            ZoomAt(e.Location, factor, imagePoint);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            if (e.Button != MouseButtons.Left) return;
            canvas.Capture = true;

            dragStart = e.Location;
            dragPanStart = pan;
            dragImageStart = ToImage(e.Location);
            roiGeometryChanged = false;
            pendingRoiHit = new RoiHit(null, RoiHitPart.None);
            if (interactionMode == InteractionMode.CreateRoiReady)
            {
                if (!enableRoiInteraction || !showRois)
                {
                    interactionMode = InteractionMode.None;
                    canvas.Cursor = Cursors.Default;
                    canvas.Capture = false;
                    return;
                }

                BeginCreateRoi(dragImageStart);
                canvas.Cursor = Cursors.Cross;
                canvas.Invalidate();
                return;
            }

            RoiHit selectedHit = enableRoiInteraction && showRois && selectedRoi != null
                ? HitTestSelectedRoi(dragImageStart)
                : new RoiHit(null, RoiHitPart.None);
            if (selectedHit.Roi != null)
            {
                SetSelectedRoi(selectedHit.Roi);
                activeRoiPart = selectedHit.Part;
                pendingRoiHit = selectedHit;
                interactionMode = InteractionMode.RoiPressed;
                canvas.Cursor = GetRoiCursor(selectedHit.Part);
                canvas.Invalidate();
                return;
            }

            RoiItem bodyRoi = enableRoiInteraction && showRois ? HitTestRoiBody(dragImageStart) : null;
            if (bodyRoi != null)
            {
                RoiHit hit = HitTestRoi(bodyRoi, dragImageStart);
                SetSelectedRoi(hit.Roi);
                activeRoiPart = hit.Part;
                pendingRoiHit = hit;
                interactionMode = InteractionMode.RoiPressed;
                canvas.Cursor = GetRoiCursor(hit.Part);
                canvas.Invalidate();
                return;
            }

            SetSelectedRoi(null);
            canvas.Invalidate();
            dragging = true;
            interactionMode = InteractionMode.Pan;
            canvas.Cursor = Cursors.SizeAll;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            PointF imagePoint = ToImage(e.Location);
            if (interactionMode == InteractionMode.CreateRoiReady)
            {
                canvas.Cursor = Cursors.Cross;
            }
            else if (interactionMode == InteractionMode.CreateRoiDragging && creatingRoi != null)
            {
                UpdateCreatingRoi(imagePoint);
                roiGeometryChanged = true;
                RoiChanged?.Invoke(owner, new RoiEventArgs(creatingRoi));
                canvas.Invalidate();
            }
            else if (interactionMode == InteractionMode.Pan && dragging)
            {
                pan = new PointF(dragPanStart.X + e.X - dragStart.X, dragPanStart.Y + e.Y - dragStart.Y);
                canvas.Invalidate();
            }
            else if (interactionMode == InteractionMode.RoiPressed && pendingRoiHit.Roi != null)
            {
                canvas.Cursor = GetRoiCursor(pendingRoiHit.Part);
                if (IsDragDistanceExceeded(e.Location))
                {
                    selectedRoi = pendingRoiHit.Roi;
                    activeRoiPart = pendingRoiHit.Part;
                    interactionMode = activeRoiPart == RoiHitPart.Body ? InteractionMode.MoveRoi : InteractionMode.ResizeRoi;
                    ApplyActiveRoiDrag(imagePoint);
                }
            }
            else if (interactionMode == InteractionMode.MoveRoi && selectedRoi != null)
            {
                ApplyActiveRoiDrag(imagePoint);
            }
            else if (interactionMode == InteractionMode.ResizeRoi && selectedRoi != null)
            {
                ApplyActiveRoiDrag(imagePoint);
            }
            else
            {
                RoiHit hit = enableRoiInteraction && showRois ? HitTestRoi(imagePoint) : new RoiHit(null, RoiHitPart.None);
                canvas.Cursor = hit.Roi == null ? Cursors.Default : GetRoiCursor(hit.Part);
            }
            UpdateStatus(e.Location);
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            canvas.Capture = false;
            if (interactionMode == InteractionMode.CreateRoiDragging)
            {
                FinishCreateRoi();
                return;
            }

            bool roiEdited = roiGeometryChanged && (interactionMode == InteractionMode.MoveRoi || interactionMode == InteractionMode.ResizeRoi);
            dragging = false;
            interactionMode = InteractionMode.None;
            activeRoiPart = RoiHitPart.None;
            pendingRoiHit = new RoiHit(null, RoiHitPart.None);
            canvas.Cursor = Cursors.Default;
            roiGeometryChanged = false;
            if (roiEdited && selectedRoi != null)
            {
                RoiChanged?.Invoke(owner, new RoiEventArgs(selectedRoi));
                RoiEditCompleted?.Invoke(owner, new RoiEventArgs(selectedRoi));
            }
            canvas.Invalidate();
        }

        private void Canvas_MouseEnter(object sender, EventArgs e)
        {
            canvas.Focus();
        }

        private void Canvas_Resize(object sender, EventArgs e)
        {
            FitImage();
        }

        private void DrawOverlays(Graphics graphics, IEnumerable<OverlayItem> items)
        {
            foreach (OverlayItem item in items)
            {
                using (Pen pen = new Pen(item.Color, Math.Max(1f, item.LineWidth)))
                using (Brush brush = new SolidBrush(item.Color))
                {
                    switch (item.Shape)
                    {
                        case OverlayShape.Rectangle:
                            graphics.DrawRectangle(pen, ToScreen(item.Bounds));
                            break;
                        case OverlayShape.Circle:
                            graphics.DrawEllipse(pen, ToScreen(item.Bounds));
                            break;
                        case OverlayShape.Line:
                            graphics.DrawLine(pen, ToScreen(item.Point1), ToScreen(item.Point2));
                            break;
                        case OverlayShape.Cross:
                            DrawCross(graphics, pen, item.Point1, item.Point2.X);
                            break;
                        case OverlayShape.Text:
                            graphics.DrawString(item.Text, item.Font, brush, ToScreen(item.Point1));
                            break;
                    }
                }
            }
        }

        private void DrawRois(Graphics graphics, bool showSelection)
        {
            foreach (RoiItem roi in rois)
            {
                roi.Draw(graphics, ToScreen, zoom, showSelection && roi == selectedRoi);
            }
        }

        /// <summary>
        /// 绘制像素网格线。
        /// </summary>
        /// <param name="graphics">绘图对象。</param>
        /// <param name="imageRect">图像显示区域。</param>
        private void DrawPixelGrid(Graphics graphics, RectangleF imageRect)
        {
            if (bitmap == null || zoom < PixelGridZoomThreshold) return;

            int firstColumn = Math.Max(0, (int)Math.Floor((-pan.X) / zoom));
            int lastColumn = Math.Min(bitmap.Width, (int)Math.Ceiling((canvas.ClientSize.Width - pan.X) / zoom));
            int firstRow = Math.Max(0, (int)Math.Floor((-pan.Y) / zoom));
            int lastRow = Math.Min(bitmap.Height, (int)Math.Ceiling((canvas.ClientSize.Height - pan.Y) / zoom));

            if (lastColumn < firstColumn || lastRow < firstRow) return;

            SmoothingMode oldSmoothingMode = graphics.SmoothingMode;
            PixelOffsetMode oldPixelOffsetMode = graphics.PixelOffsetMode;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.PixelOffsetMode = PixelOffsetMode.None;

            float left = imageRect.Left;
            float top = imageRect.Top;
            float right = imageRect.Right;
            float bottom = imageRect.Bottom;

            using (Pen pen = new Pen(Color.Black, 1f))
            {
                for (int x = firstColumn; x <= lastColumn; x++)
                {
                    float screenX = left + x * zoom;
                    graphics.DrawLine(pen, screenX, top, screenX, bottom);
                }

                for (int y = firstRow; y <= lastRow; y++)
                {
                    float screenY = top + y * zoom;
                    graphics.DrawLine(pen, left, screenY, right, screenY);
                }
            }

            graphics.SmoothingMode = oldSmoothingMode;
            graphics.PixelOffsetMode = oldPixelOffsetMode;
        }

        /// <summary>
        /// 绘制十字线。
        /// </summary>
        /// <param name="graphics">绘图对象。</param>
        /// <param name="imageRect">图像显示区域。</param>
        private static void DrawCrosshair(Graphics graphics, RectangleF imageRect)
        {
            if (imageRect.Width <= 0f || imageRect.Height <= 0f) return;

            using (Pen pen = new Pen(Color.Red, 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                float centerX = imageRect.Left + imageRect.Width / 2f;
                float centerY = imageRect.Top + imageRect.Height / 2f;
                graphics.DrawLine(pen, imageRect.Left, centerY, imageRect.Right, centerY);
                graphics.DrawLine(pen, centerX, imageRect.Top, centerX, imageRect.Bottom);
            }
        }

        private void DrawCross(Graphics graphics, Pen pen, PointF center, float size)
        {
            PointF left = ToScreen(new PointF(center.X - size, center.Y));
            PointF right = ToScreen(new PointF(center.X + size, center.Y));
            PointF top = ToScreen(new PointF(center.X, center.Y - size));
            PointF bottom = ToScreen(new PointF(center.X, center.Y + size));
            graphics.DrawLine(pen, left, right);
            graphics.DrawLine(pen, top, bottom);
        }

        private RectangleF GetImageRect()
        {
            return new RectangleF(pan.X, pan.Y, bitmap.Width * zoom, bitmap.Height * zoom);
        }

        /// <summary>
        /// 按指定中心点执行缩放。
        /// </summary>
        /// <param name="screenLocation">屏幕坐标。</param>
        /// <param name="factor">缩放倍率。</param>
        /// <param name="imagePoint">可选的图像坐标缓存。</param>
        private void ZoomAt(System.Drawing.Point screenLocation, float factor, PointF imagePoint = default(PointF))
        {
            if (bitmap == null) return;

            if (imagePoint == default(PointF))
            {
                imagePoint = ToImage(screenLocation);
            }

            zoom = Math.Max(0.01f, Math.Min(MaximumZoom, zoom * factor));
            pan = new PointF(screenLocation.X - imagePoint.X * zoom, screenLocation.Y - imagePoint.Y * zoom);
            canvas.Invalidate();
        }

        private Rectangle ToScreen(RectangleF rect)
        {
            PointF location = ToScreen(rect.Location);
            System.Drawing.Size size = new System.Drawing.Size((int)Math.Round(rect.Width * zoom), (int)Math.Round(rect.Height * zoom));
            return new Rectangle(System.Drawing.Point.Round(location), size);
        }

        private PointF ToScreen(PointF imagePoint)
        {
            return new PointF(pan.X + imagePoint.X * zoom, pan.Y + imagePoint.Y * zoom);
        }

        private PointF ToImage(System.Drawing.Point controlPoint)
        {
            return new PointF((controlPoint.X - pan.X) / zoom, (controlPoint.Y - pan.Y) / zoom);
        }

        private void UpdateStatus(System.Drawing.Point location)
        {
            if (image == null || image.Empty())
            {
                statusLabel.Text = string.Empty;
                return;
            }

            PointF point = ToImage(location);
            int x = (int)Math.Floor(point.X);
            int y = (int)Math.Floor(point.Y);
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            {
                statusLabel.Text = string.Format("图像: {0} x {1}", image.Width, image.Height);
                return;
            }

            statusLabel.Text = string.Format("图像: {0} x {1}    X: {2}    Y: {3}    灰度/颜色: {4}", image.Width, image.Height, x, y, ReadPixelText(x, y));
        }

        private string ReadPixelText(int x, int y)
        {
            try
            {
                if (image.Channels() == 1)
                {
                    return image.Get<byte>(y, x).ToString();
                }
                if (image.Channels() == 3)
                {
                    Vec3b color = image.Get<Vec3b>(y, x);
                    return string.Format("B:{0} G:{1} R:{2}", color.Item0, color.Item1, color.Item2);
                }
                if (image.Channels() == 4)
                {
                    Vec4b color = image.Get<Vec4b>(y, x);
                    return string.Format("B:{0} G:{1} R:{2} A:{3}", color.Item0, color.Item1, color.Item2, color.Item3);
                }
            }
            catch
            {
            }
            return "-";
        }

        private RoiHit HitTestRoi(PointF imagePoint)
        {
            float tolerance = GetHitTolerance();
            for (int i = rois.Count - 1; i >= 0; i--)
            {
                RoiHitPart part = rois[i].HitTest(imagePoint, tolerance);
                if (part != RoiHitPart.None) return new RoiHit(rois[i], part);
            }
            return new RoiHit(null, RoiHitPart.None);
        }

        private RoiHit HitTestRoi(RoiItem roi, PointF imagePoint)
        {
            if (roi == null) return new RoiHit(null, RoiHitPart.None);
            RoiHitPart part = roi.HitTest(imagePoint, GetHitTolerance());
            return part == RoiHitPart.None ? new RoiHit(roi, RoiHitPart.Body) : new RoiHit(roi, part);
        }

        private RoiHit HitTestSelectedRoi(PointF imagePoint)
        {
            if (selectedRoi == null) return new RoiHit(null, RoiHitPart.None);
            RoiHitPart part = selectedRoi.HitTest(imagePoint, GetHitTolerance());
            return part == RoiHitPart.None ? new RoiHit(null, RoiHitPart.None) : new RoiHit(selectedRoi, part);
        }

        private RoiItem HitTestRoiBody(PointF imagePoint)
        {
            float tolerance = GetHitTolerance();
            for (int i = rois.Count - 1; i >= 0; i--)
            {
                if (rois[i].ContainsBody(imagePoint, tolerance)) return rois[i];
            }
            return null;
        }

        private float GetHitTolerance()
        {
            return Math.Max(3f, 8f / Math.Max(zoom, 0.01f));
        }

        private bool IsRoiEditing
        {
            get
            {
                return interactionMode == InteractionMode.MoveRoi
                    || interactionMode == InteractionMode.ResizeRoi
                    || interactionMode == InteractionMode.CreateRoiDragging;
            }
        }

        private bool IsDragDistanceExceeded(System.Drawing.Point location)
        {
            System.Drawing.Size size = SystemInformation.DragSize;
            Rectangle dragBounds = new Rectangle(
                dragStart.X - size.Width / 2,
                dragStart.Y - size.Height / 2,
                size.Width,
                size.Height);
            return !dragBounds.Contains(location);
        }

        private void ApplyActiveRoiDrag(PointF imagePoint)
        {
            if (selectedRoi == null) return;
            if (interactionMode == InteractionMode.MoveRoi)
            {
                selectedRoi.Move(imagePoint.X - dragImageStart.X, imagePoint.Y - dragImageStart.Y);
                dragImageStart = imagePoint;
            }
            else if (interactionMode == InteractionMode.ResizeRoi)
            {
                selectedRoi.DragHandle(activeRoiPart, imagePoint);
            }
            roiGeometryChanged = true;
            canvas.Invalidate();
        }

        private void BeginCreateRoi(PointF startPoint)
        {
            dragImageStart = startPoint;
            creatingRoi = CreateRoiFromDrag(createRoiShape, startPoint, startPoint);
            AddRoi(creatingRoi);
            ApplyDragGeometry(creatingRoi, startPoint, startPoint);
            interactionMode = InteractionMode.CreateRoiDragging;
        }

        private void UpdateCreatingRoi(PointF currentPoint)
        {
            if (creatingRoi == null) return;
            ApplyDragGeometry(creatingRoi, dragImageStart, currentPoint);
        }

        private void FinishCreateRoi()
        {
            creatingRoi = null;
            interactionMode = InteractionMode.None;
            pendingRoiHit = new RoiHit(null, RoiHitPart.None);
            canvas.Capture = false;
            canvas.Cursor = Cursors.Default;
            canvas.Invalidate();
            if (selectedRoi != null)
            {
                RoiChanged?.Invoke(owner, new RoiEventArgs(selectedRoi));
                RoiEditCompleted?.Invoke(owner, new RoiEventArgs(selectedRoi));
            }
        }

        private static RoiItem CreateRoiFromDrag(RoiShape shape, PointF startPoint, PointF currentPoint)
        {
            switch (shape)
            {
                case RoiShape.RotatedRectangle:
                    return RoiItem.RotatedRectangle("带角度矩形ROI", startPoint, 4f, 4f, 0f);
                case RoiShape.Ring:
                    return RoiItem.Ring("圆环ROI", startPoint, 2f, 4f);
                case RoiShape.Rectangle:
                default:
                    return RoiItem.Rectangle("矩形ROI", RectangleF.FromLTRB(startPoint.X, startPoint.Y, currentPoint.X, currentPoint.Y));
            }
        }

        private static void ApplyDragGeometry(RoiItem roi, PointF startPoint, PointF currentPoint)
        {
            switch (roi.Shape)
            {
                case RoiShape.Rectangle:
                    RectangleF bounds = NormalizeRectangle(startPoint, currentPoint);
                    roi.Center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                    roi.Width = Math.Max(4f, bounds.Width);
                    roi.Height = Math.Max(4f, bounds.Height);
                    break;
                case RoiShape.RotatedRectangle:
                    RectangleF rotatedBounds = NormalizeRectangle(startPoint, currentPoint);
                    roi.Center = new PointF(rotatedBounds.Left + rotatedBounds.Width / 2f, rotatedBounds.Top + rotatedBounds.Height / 2f);
                    roi.Width = Math.Max(4f, rotatedBounds.Width);
                    roi.Height = Math.Max(4f, rotatedBounds.Height);
                    roi.Angle = 0f;
                    break;
                case RoiShape.Ring:
                    float radius = Distance(startPoint, currentPoint);
                    radius = Math.Max(4f, radius);
                    roi.Center = startPoint;
                    roi.Radius = radius;
                    roi.InnerRadius = Math.Max(2f, radius / 2f);
                    roi.StartAngle = 0f;
                    roi.SweepAngle = 360f;
                    break;
            }
        }

        private static RectangleF NormalizeRectangle(PointF startPoint, PointF endPoint)
        {
            float left = Math.Min(startPoint.X, endPoint.X);
            float top = Math.Min(startPoint.Y, endPoint.Y);
            float right = Math.Max(startPoint.X, endPoint.X);
            float bottom = Math.Max(startPoint.Y, endPoint.Y);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void SetSelectedRoi(RoiItem roi)
        {
            if (selectedRoi == roi) return;
            selectedRoi = roi;
            SelectedRoiChanged?.Invoke(owner, EventArgs.Empty);
        }

        private static Cursor GetRoiCursor(RoiHitPart part)
        {
            switch (part)
            {
                case RoiHitPart.LeftTop:
                case RoiHitPart.RightBottom:
                    return Cursors.SizeNWSE;
                case RoiHitPart.RightTop:
                case RoiHitPart.LeftBottom:
                    return Cursors.SizeNESW;
                case RoiHitPart.Left:
                case RoiHitPart.Right:
                    return Cursors.SizeWE;
                case RoiHitPart.Top:
                case RoiHitPart.Bottom:
                    return Cursors.SizeNS;
                case RoiHitPart.StartPoint:
                case RoiHitPart.EndPoint:
                case RoiHitPart.InnerRadius:
                case RoiHitPart.OuterRadius:
                case RoiHitPart.StartAngle:
                case RoiHitPart.EndAngle:
                case RoiHitPart.Rotate:
                    return Cursors.Cross;
                case RoiHitPart.Body:
                    return Cursors.SizeAll;
                default:
                    return Cursors.Default;
            }
        }

        private enum InteractionMode
        {
            None,
            Pan,
            RoiPressed,
            MoveRoi,
            ResizeRoi,
            CreateRoiReady,
            CreateRoiDragging
        }

        private struct RoiHit
        {
            public readonly RoiItem Roi;
            public readonly RoiHitPart Part;

            public RoiHit(RoiItem roi, RoiHitPart part)
            {
                Roi = roi;
                Part = part;
            }
        }
    }
}
