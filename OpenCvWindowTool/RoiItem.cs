using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpenCvWindowTool
{
    /// <summary>
    /// ROI形状类型。
    /// </summary>
    public enum RoiShape
    {
        Line,
        Rectangle,
        RotatedRectangle,
        Circle,
        Ring
    }

    /// <summary>
    /// ROI鼠标命中部位。
    /// </summary>
    public enum RoiHitPart
    {
        None,
        Body,
        StartPoint,
        EndPoint,
        LeftTop,
        RightTop,
        RightBottom,
        LeftBottom,
        Left,
        Top,
        Right,
        Bottom,
        InnerRadius,
        OuterRadius,
        StartAngle,
        EndAngle,
        Rotate
    }

    /// <summary>
    /// ROI数据对象，负责保存ROI参数、绘制、命中检测和属性变化刷新通知。
    /// </summary>
    public sealed class RoiItem
    {
        private const float MinimumSize = 4f;
        private const float RotateHandleDistance = 36f;
        private string name;
        private PointF center;
        private PointF point1;
        private PointF point2;
        private float width;
        private float height;
        private float radius;
        private float innerRadius;
        private float startAngle;
        private float sweepAngle;
        private float angle;
        private Color color;
        private float lineWidth = float.NaN;

        /// <summary>
        /// 创建ROI数据对象。
        /// </summary>
        /// <param name="shape">ROI形状。</param>
        private RoiItem(RoiShape shape)
        {
            Shape = shape;
            name = shape.ToString();
            color = Color.DeepSkyBlue;
            width = 120f;
            height = 80f;
            radius = 50f;
            innerRadius = 30f;
            startAngle = 0f;
            sweepAngle = 360f;
        }

        /// <summary>
        /// ROI刷新显示事件。
        /// </summary>
        internal event Action RefreshDisplay;

        /// <summary>
        /// ROI名称。
        /// </summary>
        public string Name
        {
            get { return name; }
            set { SetValue(ref name, value); }
        }

        /// <summary>
        /// ROI形状。
        /// </summary>
        public RoiShape Shape { get; private set; }

        /// <summary>
        /// ROI中心点。
        /// </summary>
        public PointF Center
        {
            get { return center; }
            set { SetValue(ref center, value); }
        }

        /// <summary>
        /// 线段起点。
        /// </summary>
        public PointF Point1
        {
            get { return point1; }
            set { SetValue(ref point1, value); }
        }

        /// <summary>
        /// 线段终点。
        /// </summary>
        public PointF Point2
        {
            get { return point2; }
            set { SetValue(ref point2, value); }
        }

        /// <summary>
        /// ROI宽度。
        /// </summary>
        public float Width
        {
            get { return width; }
            set { SetValue(ref width, value); }
        }

        /// <summary>
        /// ROI高度。
        /// </summary>
        public float Height
        {
            get { return height; }
            set { SetValue(ref height, value); }
        }

        /// <summary>
        /// 圆或圆环外半径。
        /// </summary>
        public float Radius
        {
            get { return radius; }
            set { SetValue(ref radius, value); }
        }

        /// <summary>
        /// 圆环内半径。
        /// </summary>
        public float InnerRadius
        {
            get { return innerRadius; }
            set { SetValue(ref innerRadius, value); }
        }

        /// <summary>
        /// 圆环起始角度。
        /// </summary>
        public float StartAngle
        {
            get { return startAngle; }
            set { SetValue(ref startAngle, value); }
        }

        /// <summary>
        /// 圆环扫过角度。
        /// </summary>
        public float SweepAngle
        {
            get { return sweepAngle; }
            set { SetValue(ref sweepAngle, value); }
        }

        /// <summary>
        /// 旋转矩形角度。
        /// </summary>
        public float Angle
        {
            get { return angle; }
            set { SetValue(ref angle, value); }
        }

        /// <summary>
        /// ROI显示颜色。
        /// </summary>
        public Color Color
        {
            get { return color; }
            set { SetValue(ref color, value); }
        }

        /// <summary>
        /// 单个ROI线宽，未设置时使用全局线宽。
        /// </summary>
        public float LineWidth
        {
            get { return lineWidth; }
            set { SetValue(ref lineWidth, value); }
        }

        /// <summary>
        /// 实际绘制线宽。
        /// </summary>
        internal float EffectiveLineWidth
        {
            get { return float.IsNaN(lineWidth) || lineWidth <= 0f ? RoiStyle.GlobalLineWidth : lineWidth; }
        }

        /// <summary>
        /// 创建线ROI。
        /// </summary>
        public static RoiItem Line(string name, PointF start, PointF end)
        {
            RoiItem roi = new RoiItem(RoiShape.Line);
            roi.name = name;
            roi.point1 = start;
            roi.point2 = end;
            roi.center = Mid(start, end);
            return roi;
        }

        /// <summary>
        /// 创建不带角度的矩形ROI。
        /// </summary>
        public static RoiItem Rectangle(string name, RectangleF bounds)
        {
            RoiItem roi = new RoiItem(RoiShape.Rectangle);
            roi.name = name;
            roi.center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
            roi.width = Math.Max(MinimumSize, bounds.Width);
            roi.height = Math.Max(MinimumSize, bounds.Height);
            return roi;
        }

        /// <summary>
        /// 创建带角度的矩形ROI。
        /// </summary>
        public static RoiItem RotatedRectangle(string name, PointF center, float width, float height, float angle)
        {
            RoiItem roi = new RoiItem(RoiShape.RotatedRectangle);
            roi.name = name;
            roi.center = center;
            roi.width = Math.Max(MinimumSize, width);
            roi.height = Math.Max(MinimumSize, height);
            roi.angle = angle;
            return roi;
        }

        /// <summary>
        /// 创建圆ROI。
        /// </summary>
        public static RoiItem Circle(string name, PointF center, float radius)
        {
            RoiItem roi = new RoiItem(RoiShape.Circle);
            roi.name = name;
            roi.center = center;
            roi.radius = Math.Max(2f, radius);
            return roi;
        }

        /// <summary>
        /// 创建圆环ROI，默认显示完整360度圆环。
        /// </summary>
        public static RoiItem Ring(string name, PointF center, float innerRadius, float outerRadius)
        {
            RoiItem roi = new RoiItem(RoiShape.Ring);
            roi.name = name;
            roi.center = center;
            roi.innerRadius = Math.Max(1f, Math.Min(innerRadius, outerRadius - 1f));
            roi.radius = Math.Max(roi.innerRadius + 1f, outerRadius);
            roi.startAngle = 0f;
            roi.sweepAngle = 360f;
            return roi;
        }

        /// <summary>
        /// 判断ROI是否可以作为直线检测测量框。
        /// </summary>
        public bool CanDetectLine()
        {
            return Shape == RoiShape.Rectangle || Shape == RoiShape.RotatedRectangle;
        }

        /// <summary>
        /// 获取直线检测测量框，普通矩形按0度处理。
        /// </summary>
        public LineDetectionFrame ToLineDetectionFrame()
        {
            if (!CanDetectLine()) throw new InvalidOperationException("直线检测只支持普通矩形ROI和带角度矩形ROI。");
            return new LineDetectionFrame(center, width, height, Shape == RoiShape.RotatedRectangle ? angle : 0f);
        }

        /// <summary>
        /// 移动ROI。
        /// </summary>
        public void Move(float dx, float dy)
        {
            center = Offset(center, dx, dy);
            point1 = Offset(point1, dx, dy);
            point2 = Offset(point2, dx, dy);
            OnRefreshDisplay();
        }

        /// <summary>
        /// 拖动ROI控制点。
        /// </summary>
        public void DragHandle(RoiHitPart part, PointF imagePoint)
        {
            switch (Shape)
            {
                case RoiShape.Line:
                    DragLine(part, imagePoint);
                    break;
                case RoiShape.Rectangle:
                    ResizeAxisRectangle(part, imagePoint);
                    break;
                case RoiShape.RotatedRectangle:
                    DragRotatedRectangle(part, imagePoint);
                    break;
                case RoiShape.Circle:
                    radius = Math.Max(2f, Distance(center, imagePoint));
                    break;
                case RoiShape.Ring:
                    DragRing(part, imagePoint);
                    break;
            }
            OnRefreshDisplay();
        }

        /// <summary>
        /// 检查图像坐标是否命中ROI。
        /// </summary>
        public RoiHitPart HitTest(PointF imagePoint, float tolerance)
        {
            foreach (KeyValuePair<RoiHitPart, PointF> handle in GetHandles())
            {
                if (Distance(handle.Value, imagePoint) <= tolerance) return handle.Key;
            }

            switch (Shape)
            {
                case RoiShape.Line:
                    return DistanceToSegment(imagePoint, point1, point2) <= tolerance ? RoiHitPart.Body : RoiHitPart.None;
                case RoiShape.Rectangle:
                    RoiHitPart axisEdge = HitAxisRectangleEdge(imagePoint, tolerance);
                    if (axisEdge != RoiHitPart.None) return axisEdge;
                    return GetAxisBounds().Contains(imagePoint) ? RoiHitPart.Body : RoiHitPart.None;
                case RoiShape.RotatedRectangle:
                    RoiHitPart rotatedEdge = HitRotatedRectangleEdge(imagePoint, tolerance);
                    if (rotatedEdge != RoiHitPart.None) return rotatedEdge;
                    using (GraphicsPath path = GetRotatedPath())
                    {
                        return path.IsVisible(imagePoint) ? RoiHitPart.Body : RoiHitPart.None;
                    }
                case RoiShape.Circle:
                    return Distance(center, imagePoint) <= radius ? RoiHitPart.Body : RoiHitPart.None;
                case RoiShape.Ring:
                    return HitRingBody(imagePoint) ? RoiHitPart.Body : RoiHitPart.None;
                default:
                    return RoiHitPart.None;
            }
        }

        /// <summary>
        /// 判断鼠标是否落在ROI本体内部，外部控制点不参与选中判断。
        /// </summary>
        public bool ContainsBody(PointF imagePoint, float tolerance)
        {
            switch (Shape)
            {
                case RoiShape.Line:
                    return DistanceToSegment(imagePoint, point1, point2) <= tolerance;
                case RoiShape.Rectangle:
                    return GetAxisBounds().Contains(imagePoint);
                case RoiShape.RotatedRectangle:
                    using (GraphicsPath path = GetRotatedPath())
                    {
                        return path.IsVisible(imagePoint);
                    }
                case RoiShape.Circle:
                    return Distance(center, imagePoint) <= radius;
                case RoiShape.Ring:
                    return HitRingBody(imagePoint);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 绘制ROI。
        /// </summary>
        public void Draw(Graphics graphics, Func<PointF, PointF> toScreen, float zoom, bool selected)
        {
            using (Pen pen = new Pen(color, Math.Max(1f, EffectiveLineWidth)))
            using (Brush brush = new SolidBrush(color))
            {
                switch (Shape)
                {
                    case RoiShape.Line:
                        graphics.DrawLine(pen, toScreen(point1), toScreen(point2));
                        break;
                    case RoiShape.Rectangle:
                        graphics.DrawRectangle(pen, ToScreenRect(GetAxisBounds(), toScreen));
                        break;
                    case RoiShape.RotatedRectangle:
                        graphics.DrawPolygon(pen, ToScreenPoints(GetRotatedCorners(), toScreen));
                        break;
                    case RoiShape.Circle:
                        graphics.DrawEllipse(pen, ToScreenCircle(center, radius, toScreen, zoom));
                        break;
                    case RoiShape.Ring:
                        DrawRing(graphics, pen, toScreen, zoom);
                        break;
                }

                if (selected)
                {
                    foreach (KeyValuePair<RoiHitPart, PointF> handle in GetHandles())
                    {
                        DrawHandle(graphics, brush, toScreen(handle.Value));
                    }
                }
            }
        }

        private void SetValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnRefreshDisplay();
        }

        private void OnRefreshDisplay()
        {
            RefreshDisplay?.Invoke();
        }

        private IEnumerable<KeyValuePair<RoiHitPart, PointF>> GetHandles()
        {
            switch (Shape)
            {
                case RoiShape.Line:
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.StartPoint, point1);
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.EndPoint, point2);
                    break;
                case RoiShape.Rectangle:
                    foreach (KeyValuePair<RoiHitPart, PointF> item in GetAxisRectangleHandles()) yield return item;
                    break;
                case RoiShape.RotatedRectangle:
                    foreach (KeyValuePair<RoiHitPart, PointF> item in GetRotatedRectangleHandles()) yield return item;
                    break;
                case RoiShape.Circle:
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.OuterRadius, new PointF(center.X + radius, center.Y));
                    break;
                case RoiShape.Ring:
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.InnerRadius, PointOnCircle(innerRadius, startAngle));
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.OuterRadius, PointOnCircle(radius, startAngle));
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.StartAngle, PointOnCircle(AngleHandleRadius(), startAngle));
                    yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.EndAngle, PointOnCircle(AngleHandleRadius(), EndAngleForHandle()));
                    break;
            }
        }

        private IEnumerable<KeyValuePair<RoiHitPart, PointF>> GetAxisRectangleHandles()
        {
            RectangleF bounds = GetAxisBounds();
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.LeftTop, new PointF(bounds.Left, bounds.Top));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.RightTop, new PointF(bounds.Right, bounds.Top));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.RightBottom, new PointF(bounds.Right, bounds.Bottom));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.LeftBottom, new PointF(bounds.Left, bounds.Bottom));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Left, new PointF(bounds.Left, bounds.Top + bounds.Height / 2f));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Top, new PointF(bounds.Left + bounds.Width / 2f, bounds.Top));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Right, new PointF(bounds.Right, bounds.Top + bounds.Height / 2f));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Bottom, new PointF(bounds.Left + bounds.Width / 2f, bounds.Bottom));
        }

        private IEnumerable<KeyValuePair<RoiHitPart, PointF>> GetRotatedRectangleHandles()
        {
            PointF[] corners = GetRotatedCorners();
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.LeftTop, corners[0]);
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.RightTop, corners[1]);
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.RightBottom, corners[2]);
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.LeftBottom, corners[3]);
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Left, Mid(corners[0], corners[3]));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Top, Mid(corners[0], corners[1]));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Right, Mid(corners[1], corners[2]));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Bottom, Mid(corners[2], corners[3]));
            yield return new KeyValuePair<RoiHitPart, PointF>(RoiHitPart.Rotate, RotateHandlePoint());
        }

        private void DragLine(RoiHitPart part, PointF imagePoint)
        {
            if (part == RoiHitPart.StartPoint) point1 = imagePoint;
            else if (part == RoiHitPart.EndPoint) point2 = imagePoint;
            else Move(imagePoint.X - center.X, imagePoint.Y - center.Y);
            center = Mid(point1, point2);
        }

        private void ResizeAxisRectangle(RoiHitPart part, PointF imagePoint)
        {
            RectangleF bounds = GetAxisBounds();
            float left = bounds.Left;
            float top = bounds.Top;
            float right = bounds.Right;
            float bottom = bounds.Bottom;

            if (part == RoiHitPart.LeftTop || part == RoiHitPart.LeftBottom || part == RoiHitPart.Left) left = imagePoint.X;
            if (part == RoiHitPart.RightTop || part == RoiHitPart.RightBottom || part == RoiHitPart.Right) right = imagePoint.X;
            if (part == RoiHitPart.LeftTop || part == RoiHitPart.RightTop || part == RoiHitPart.Top) top = imagePoint.Y;
            if (part == RoiHitPart.LeftBottom || part == RoiHitPart.RightBottom || part == RoiHitPart.Bottom) bottom = imagePoint.Y;

            width = Math.Max(MinimumSize, Math.Abs(right - left));
            height = Math.Max(MinimumSize, Math.Abs(bottom - top));
            center = new PointF((left + right) / 2f, (top + bottom) / 2f);
        }

        private void DragRotatedRectangle(RoiHitPart part, PointF imagePoint)
        {
            if (part == RoiHitPart.Rotate)
            {
                angle = NormalizeAngle(AngleOf(center, imagePoint) + 90f);
                return;
            }

            if (IsEdge(part))
            {
                DragRotatedRectangleEdge(part, imagePoint);
                return;
            }

            if (!IsCorner(part)) return;
            PointF opposite = OppositeCorner(part);
            PointF localOpposite = WorldToLocal(opposite);
            PointF localPoint = WorldToLocal(imagePoint);
            PointF localCenter = Mid(localOpposite, localPoint);

            width = Math.Max(MinimumSize, Math.Abs(localPoint.X - localOpposite.X));
            height = Math.Max(MinimumSize, Math.Abs(localPoint.Y - localOpposite.Y));
            center = LocalOffsetToWorld(localCenter);
        }

        private void DragRing(RoiHitPart part, PointF imagePoint)
        {
            float distance = Distance(center, imagePoint);
            if (part == RoiHitPart.InnerRadius)
            {
                innerRadius = Math.Max(1f, Math.Min(radius - 1f, distance));
            }
            else if (part == RoiHitPart.StartAngle)
            {
                float oldEnd = IsFullRing() ? EndAngleForHandle() : NormalizeAngle(startAngle + sweepAngle);
                startAngle = AngleOf(center, imagePoint);
                sweepAngle = NormalizeSweep(oldEnd - startAngle);
            }
            else if (part == RoiHitPart.EndAngle)
            {
                sweepAngle = NormalizeSweep(AngleOf(center, imagePoint) - startAngle);
            }
            else
            {
                radius = Math.Max(innerRadius + 1f, distance);
            }
        }

        private bool HitRingBody(PointF imagePoint)
        {
            float distance = Distance(center, imagePoint);
            if (distance < innerRadius || distance > radius) return false;
            return IsFullRing() || IsInAngle(imagePoint);
        }

        private RoiHitPart HitAxisRectangleEdge(PointF imagePoint, float tolerance)
        {
            RectangleF bounds = GetAxisBounds();
            bool inVertical = imagePoint.Y >= bounds.Top - tolerance && imagePoint.Y <= bounds.Bottom + tolerance;
            bool inHorizontal = imagePoint.X >= bounds.Left - tolerance && imagePoint.X <= bounds.Right + tolerance;
            if (inVertical && Math.Abs(imagePoint.X - bounds.Left) <= tolerance) return RoiHitPart.Left;
            if (inVertical && Math.Abs(imagePoint.X - bounds.Right) <= tolerance) return RoiHitPart.Right;
            if (inHorizontal && Math.Abs(imagePoint.Y - bounds.Top) <= tolerance) return RoiHitPart.Top;
            if (inHorizontal && Math.Abs(imagePoint.Y - bounds.Bottom) <= tolerance) return RoiHitPart.Bottom;
            return RoiHitPart.None;
        }

        private RoiHitPart HitRotatedRectangleEdge(PointF imagePoint, float tolerance)
        {
            PointF local = WorldToLocal(imagePoint);
            float halfWidth = width / 2f;
            float halfHeight = height / 2f;
            bool inVertical = local.Y >= -halfHeight - tolerance && local.Y <= halfHeight + tolerance;
            bool inHorizontal = local.X >= -halfWidth - tolerance && local.X <= halfWidth + tolerance;
            if (inVertical && Math.Abs(local.X + halfWidth) <= tolerance) return RoiHitPart.Left;
            if (inVertical && Math.Abs(local.X - halfWidth) <= tolerance) return RoiHitPart.Right;
            if (inHorizontal && Math.Abs(local.Y + halfHeight) <= tolerance) return RoiHitPart.Top;
            if (inHorizontal && Math.Abs(local.Y - halfHeight) <= tolerance) return RoiHitPart.Bottom;
            return RoiHitPart.None;
        }

        private void DragRotatedRectangleEdge(RoiHitPart part, PointF imagePoint)
        {
            PointF localPoint = WorldToLocal(imagePoint);
            float left = -width / 2f;
            float right = width / 2f;
            float top = -height / 2f;
            float bottom = height / 2f;

            if (part == RoiHitPart.Left) left = localPoint.X;
            if (part == RoiHitPart.Right) right = localPoint.X;
            if (part == RoiHitPart.Top) top = localPoint.Y;
            if (part == RoiHitPart.Bottom) bottom = localPoint.Y;

            width = Math.Max(MinimumSize, Math.Abs(right - left));
            height = Math.Max(MinimumSize, Math.Abs(bottom - top));
            center = LocalOffsetToWorld(new PointF((left + right) / 2f, (top + bottom) / 2f));
        }

        private void DrawRing(Graphics graphics, Pen pen, Func<PointF, PointF> toScreen, float zoom)
        {
            if (IsFullRing())
            {
                graphics.DrawEllipse(pen, ToScreenCircle(center, radius, toScreen, zoom));
                graphics.DrawEllipse(pen, ToScreenCircle(center, innerRadius, toScreen, zoom));
                return;
            }

            graphics.DrawArc(pen, ToScreenCircle(center, radius, toScreen, zoom), startAngle, sweepAngle);
            graphics.DrawArc(pen, ToScreenCircle(center, innerRadius, toScreen, zoom), startAngle, sweepAngle);
            graphics.DrawLine(pen, toScreen(PointOnCircle(innerRadius, startAngle)), toScreen(PointOnCircle(radius, startAngle)));
            graphics.DrawLine(pen, toScreen(PointOnCircle(innerRadius, startAngle + sweepAngle)), toScreen(PointOnCircle(radius, startAngle + sweepAngle)));
        }

        private RectangleF GetAxisBounds()
        {
            return new RectangleF(center.X - width / 2f, center.Y - height / 2f, width, height);
        }

        private GraphicsPath GetRotatedPath()
        {
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(GetRotatedCorners());
            return path;
        }

        private PointF[] GetRotatedCorners()
        {
            PointF[] points =
            {
                new PointF(-width / 2f, -height / 2f),
                new PointF(width / 2f, -height / 2f),
                new PointF(width / 2f, height / 2f),
                new PointF(-width / 2f, height / 2f)
            };

            for (int i = 0; i < points.Length; i++)
            {
                points[i] = LocalOffsetToWorld(points[i]);
            }

            return points;
        }

        private PointF RotateHandlePoint()
        {
            return LocalOffsetToWorld(new PointF(0f, -height / 2f - RotateHandleDistance));
        }

        private PointF OppositeCorner(RoiHitPart part)
        {
            PointF[] corners = GetRotatedCorners();
            switch (part)
            {
                case RoiHitPart.LeftTop:
                    return corners[2];
                case RoiHitPart.RightTop:
                    return corners[3];
                case RoiHitPart.RightBottom:
                    return corners[0];
                case RoiHitPart.LeftBottom:
                    return corners[1];
                default:
                    return center;
            }
        }

        private bool IsInAngle(PointF imagePoint)
        {
            float currentAngle = NormalizeAngle(AngleOf(center, imagePoint));
            float start = NormalizeAngle(startAngle);
            float end = NormalizeAngle(startAngle + sweepAngle);
            if (start <= end) return currentAngle >= start && currentAngle <= end;
            return currentAngle >= start || currentAngle <= end;
        }

        private bool IsFullRing()
        {
            return sweepAngle >= 359.9f;
        }

        private float EndAngleForHandle()
        {
            return IsFullRing() ? startAngle + 90f : startAngle + sweepAngle;
        }

        private float AngleHandleRadius()
        {
            return (innerRadius + radius) / 2f;
        }

        private PointF PointOnCircle(float targetRadius, float targetAngle)
        {
            double radians = targetAngle * Math.PI / 180d;
            return new PointF(center.X + targetRadius * (float)Math.Cos(radians), center.Y + targetRadius * (float)Math.Sin(radians));
        }

        private PointF WorldToLocal(PointF point)
        {
            PointF offset = new PointF(point.X - center.X, point.Y - center.Y);
            return RotateOffset(offset, -angle);
        }

        private PointF LocalOffsetToWorld(PointF offset)
        {
            PointF rotated = RotateOffset(offset, angle);
            return new PointF(center.X + rotated.X, center.Y + rotated.Y);
        }

        private static void DrawHandle(Graphics graphics, Brush brush, PointF point)
        {
            RectangleF rect = new RectangleF(point.X - 4f, point.Y - 4f, 8f, 8f);
            graphics.FillRectangle(brush, rect);
            graphics.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static bool IsCorner(RoiHitPart part)
        {
            return part == RoiHitPart.LeftTop || part == RoiHitPart.RightTop || part == RoiHitPart.RightBottom || part == RoiHitPart.LeftBottom;
        }

        private static bool IsEdge(RoiHitPart part)
        {
            return part == RoiHitPart.Left || part == RoiHitPart.Top || part == RoiHitPart.Right || part == RoiHitPart.Bottom;
        }

        private static Rectangle ToScreenRect(RectangleF rect, Func<PointF, PointF> toScreen)
        {
            PointF leftTop = toScreen(rect.Location);
            PointF rightBottom = toScreen(new PointF(rect.Right, rect.Bottom));
            return System.Drawing.Rectangle.Round(RectangleF.FromLTRB(leftTop.X, leftTop.Y, rightBottom.X, rightBottom.Y));
        }

        private static RectangleF ToScreenCircle(PointF targetCenter, float targetRadius, Func<PointF, PointF> toScreen, float zoom)
        {
            PointF screenCenter = toScreen(targetCenter);
            float screenRadius = targetRadius * zoom;
            return new RectangleF(screenCenter.X - screenRadius, screenCenter.Y - screenRadius, screenRadius * 2f, screenRadius * 2f);
        }

        private static PointF[] ToScreenPoints(PointF[] points, Func<PointF, PointF> toScreen)
        {
            PointF[] result = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = toScreen(points[i]);
            return result;
        }

        private static PointF RotateOffset(PointF point, float targetAngle)
        {
            double radians = targetAngle * Math.PI / 180d;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new PointF(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos);
        }

        private static PointF Offset(PointF point, float dx, float dy)
        {
            return new PointF(point.X + dx, point.Y + dy);
        }

        private static PointF Mid(PointF a, PointF b)
        {
            return new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            if (dx == 0f && dy == 0f) return Distance(p, a);
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0f, Math.Min(1f, t));
            return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        private static float AngleOf(PointF targetCenter, PointF point)
        {
            return NormalizeAngle((float)(Math.Atan2(point.Y - targetCenter.Y, point.X - targetCenter.X) * 180d / Math.PI));
        }

        private static float NormalizeAngle(float targetAngle)
        {
            targetAngle %= 360f;
            if (targetAngle < 0f) targetAngle += 360f;
            return targetAngle;
        }

        private static float NormalizeSweep(float targetAngle)
        {
            targetAngle = NormalizeAngle(targetAngle);
            return targetAngle <= 0.1f ? 360f : targetAngle;
        }
    }
}
