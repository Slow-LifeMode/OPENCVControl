using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 边缘极性。
    /// </summary>
    public enum LineEdgePolarity
    {
        Positive,
        Negative,
        Any
    }

    /// <summary>
    /// 边缘强度计算类型。
    /// </summary>
    public enum LineEdgeStrengthType
    {
        Gradient1D,
        Sobel
    }

    /// <summary>
    /// 每条扫描线上候选边缘的选择方式。
    /// </summary>
    public enum LineSelectionMode
    {
        First,
        Strongest,
        Last
    }

    /// <summary>
    /// 直线检测扫描方向。
    /// </summary>
    public enum LineScanDirection
    {
        LeftToRight,
        TopToBottom,
        BottomToTop,
        RightToLeft
    }

    /// <summary>
    /// 直线拟合方式。
    /// </summary>
    public enum LineFitMode
    {
        Local,
        LeastSquares,
        Huber,
        Ransac,
        Robust = Huber
    }

    /// <summary>
    /// 直线检测运行模式。
    /// </summary>
    public enum LineDetectionMode
    {
        SelfMode,
        OPTMode
    }

    /// <summary>
    /// 直线检测参数。
    /// </summary>
    public sealed class LineDetectionParams
    {
        public LineDetectionParams()
        {
            EdgeThreshold = 20f;
            SampleCount = 40;
            SampleStep = 1f;
            SmoothSize = 3;
            EdgeWidth = 1;
            ProjectionWidth = 1;
            RejectRatio = 20;
            RejectDistance = 5;
            ProfileLineIndex = 1;
            ShowSearchLines = true;
            EdgePolarity = LineEdgePolarity.Any;
            StrengthType = LineEdgeStrengthType.Gradient1D;
            SelectionMode = LineSelectionMode.Strongest;
            ScanDirection = LineScanDirection.LeftToRight;
            FitMode = LineFitMode.LeastSquares;
        }

        /// <summary>
        /// 边缘强度阈值。
        /// </summary>
        public float EdgeThreshold { get; set; }

        /// <summary>
        /// 检测点数量，也就是扫描线数量。
        /// </summary>
        public int SampleCount { get; set; }

        /// <summary>
        /// 扫描方向采样间隔，单位为像素。
        /// </summary>
        public float SampleStep { get; set; }

        /// <summary>
        /// 一维灰度曲线平滑窗口，奇数生效。
        /// </summary>
        public int SmoothSize { get; set; }

        /// <summary>
        /// 边缘宽度，用于控制一维梯度的计算跨度。
        /// </summary>
        public int EdgeWidth { get; set; }

        /// <summary>
        /// 投影宽度，用于控制每条搜索线两侧参与灰度平均的像素宽度。
        /// </summary>
        public int ProjectionWidth { get; set; }

        /// <summary>
        /// 剔除比例，限制最多可剔除的外点比例。
        /// </summary>
        public int RejectRatio { get; set; }

        /// <summary>
        /// 剔除距离，单位为像素。
        /// </summary>
        public int RejectDistance { get; set; }

        /// <summary>
        /// 剖面图对应的搜索线编号，从1开始。
        /// </summary>
        public int ProfileLineIndex { get; set; }

        /// <summary>
        /// 是否显示搜索线。
        /// </summary>
        public bool ShowSearchLines { get; set; }

        /// <summary>
        /// 边缘极性。
        /// </summary>
        public LineEdgePolarity EdgePolarity { get; set; }

        /// <summary>
        /// 边缘强度计算类型。
        /// </summary>
        public LineEdgeStrengthType StrengthType { get; set; }

        /// <summary>
        /// 候选边缘选择方式。
        /// </summary>
        public LineSelectionMode SelectionMode { get; set; }

        /// <summary>
        /// ROI内部扫描方向。
        /// </summary>
        public LineScanDirection ScanDirection { get; set; }

        /// <summary>
        /// 拟合直线方式。
        /// </summary>
        public LineFitMode FitMode { get; set; }
    }

    /// <summary>
    /// 直线检测ROI测量框。
    /// </summary>
    public struct LineDetectionFrame
    {
        public readonly PointF Center;
        public readonly float Width;
        public readonly float Height;
        public readonly float Angle;

        public LineDetectionFrame(PointF center, float width, float height, float angle)
        {
            Center = center;
            Width = Math.Max(1f, width);
            Height = Math.Max(1f, height);
            Angle = angle;
        }

        /// <summary>
        /// ROI局部X方向。
        /// </summary>
        public PointF XDirection => UnitVector(Angle);

        /// <summary>
        /// ROI局部Y方向。
        /// </summary>
        public PointF YDirection
        {
            get
            {
                PointF x = XDirection;
                return new PointF(-x.Y, x.X);
            }
        }

        /// <summary>
        /// 测量框是否有效。
        /// </summary>
        public bool IsValid => Width > 0f && Height > 0f;

        /// <summary>
        /// 获取扫描方向向量。
        /// </summary>
        public PointF GetScanDirection(LineScanDirection direction)
        {
            PointF x = XDirection;
            PointF y = YDirection;
            switch (direction)
            {
                case LineScanDirection.RightToLeft:
                    return new PointF(-x.X, -x.Y);
                case LineScanDirection.TopToBottom:
                    return y;
                case LineScanDirection.BottomToTop:
                    return new PointF(-y.X, -y.Y);
                default:
                    return x;
            }
        }

        /// <summary>
        /// 获取扫描线排列方向，始终垂直于扫描方向。
        /// </summary>
        public PointF GetLineArrangeDirection(LineScanDirection direction)
        {
            PointF scan = GetScanDirection(direction);
            return new PointF(-scan.Y, scan.X);
        }

        /// <summary>
        /// 获取扫描方向长度。
        /// </summary>
        public float GetScanLength(LineScanDirection direction)
        {
            return direction == LineScanDirection.LeftToRight || direction == LineScanDirection.RightToLeft ? Width : Height;
        }

        /// <summary>
        /// 获取扫描线排列方向长度。
        /// </summary>
        public float GetArrangeLength(LineScanDirection direction)
        {
            return direction == LineScanDirection.LeftToRight || direction == LineScanDirection.RightToLeft ? Height : Width;
        }

        /// <summary>
        /// 获取方向箭头。
        /// </summary>
        public void GetDirectionArrow(LineScanDirection direction, out PointF start, out PointF end)
        {
            PointF dir = GetScanDirection(direction);
            float length = Math.Max(16f, GetScanLength(direction) * 0.45f);
            start = new PointF(Center.X - dir.X * length * 0.5f, Center.Y - dir.Y * length * 0.5f);
            end = new PointF(Center.X + dir.X * length * 0.5f, Center.Y + dir.Y * length * 0.5f);
        }

        /// <summary>
        /// 获取测量框四个角点。
        /// </summary>
        public PointF[] GetCorners()
        {
            PointF x = XDirection;
            PointF y = YDirection;
            float halfWidth = Width / 2f;
            float halfHeight = Height / 2f;
            return new[]
            {
                Offset(Center, x, y, -halfWidth, -halfHeight),
                Offset(Center, x, y, halfWidth, -halfHeight),
                Offset(Center, x, y, halfWidth, halfHeight),
                Offset(Center, x, y, -halfWidth, halfHeight)
            };
        }

        private static PointF UnitVector(float angle)
        {
            double radians = angle * Math.PI / 180d;
            return new PointF((float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        private static PointF Offset(PointF center, PointF x, PointF y, float xDistance, float yDistance)
        {
            return new PointF(center.X + x.X * xDistance + y.X * yDistance, center.Y + x.Y * xDistance + y.Y * yDistance);
        }
    }

    /// <summary>
    /// 单个检测点数据。
    /// </summary>
    public sealed class LineEdgePoint
    {
        public LineEdgePoint(PointF point, float strength)
        {
            Point = point;
            Strength = strength;
        }

        public PointF Point { get; private set; }

        public float Strength { get; private set; }
    }

    /// <summary>
    /// 直线检测结果。
    /// </summary>
    public sealed class LineDetectionResult
    {
        private LineDetectionResult(bool success, string message, LineDetectionFrame frame, LineScanDirection scanDirection, PointF lineStart, PointF lineEnd, List<LineEdgePoint> edgePoints, TimeSpan elapsed)
            : this(success, message, frame, scanDirection, lineStart, new PointF((lineStart.X + lineEnd.X) * 0.5f, (lineStart.Y + lineEnd.Y) * 0.5f), lineEnd, edgePoints, null, null, elapsed)
        {
        }

        private LineDetectionResult(bool success, string message, LineDetectionFrame frame, LineScanDirection scanDirection, PointF lineStart, PointF lineMiddle, PointF lineEnd, List<LineEdgePoint> edgePoints, List<LineEdgePoint> firstPoints, List<LineEdgePoint> lastPoints, TimeSpan elapsed)
        {
            Success = success;
            Message = message;
            Frame = frame;
            ScanDirection = scanDirection;
            LineStart = lineStart;
            LineMiddle = lineMiddle;
            LineEnd = lineEnd;
            EdgePoints = (edgePoints ?? new List<LineEdgePoint>()).AsReadOnly();
            FirstPoints = (firstPoints ?? new List<LineEdgePoint>()).AsReadOnly();
            LastPoints = (lastPoints ?? new List<LineEdgePoint>()).AsReadOnly();
            Elapsed = elapsed;
            Angle = success ? CalculateAngle(lineStart, lineEnd) : 0f;
            AverageStrength = EdgePoints.Count == 0 ? 0f : EdgePoints.Average(x => x.Strength);
            MaxStrength = EdgePoints.Count == 0 ? 0f : EdgePoints.Max(x => x.Strength);
        }

        public bool Success { get; private set; }

        public string Message { get; private set; }

        public LineDetectionFrame Frame { get; private set; }

        public LineScanDirection ScanDirection { get; private set; }

        public PointF LineStart { get; private set; }

        public PointF LineMiddle { get; private set; }

        public PointF LineEnd { get; private set; }

        public float Angle { get; private set; }

        public float AverageStrength { get; private set; }

        public float MaxStrength { get; private set; }

        public TimeSpan Elapsed { get; private set; }

        public IReadOnlyList<LineEdgePoint> EdgePoints { get; private set; }

        public IReadOnlyList<LineEdgePoint> FirstPoints { get; private set; }

        public IReadOnlyList<LineEdgePoint> LastPoints { get; private set; }

        /// <summary>
        /// 创建成功的直线检测结果。
        /// </summary>
        /// <param name="frame">检测测量框。</param>
        /// <param name="scanDirection">扫描方向。</param>
        /// <param name="lineStart">结果线段起点。</param>
        /// <param name="lineEnd">结果线段终点。</param>
        /// <param name="edgePoints">参与拟合的边缘点。</param>
        /// <param name="elapsed">检测耗时。</param>
        /// <returns>成功的直线检测结果。</returns>
        public static LineDetectionResult CreateSuccess(LineDetectionFrame frame, LineScanDirection scanDirection, PointF lineStart, PointF lineEnd, List<LineEdgePoint> edgePoints, TimeSpan elapsed)
        {
            return new LineDetectionResult(true, "检测成功", frame, scanDirection, lineStart, lineEnd, edgePoints, elapsed);
        }

        /// <summary>
        /// 创建成功的直线检测结果。
        /// </summary>
        public static LineDetectionResult CreateSuccess(LineDetectionFrame frame, LineScanDirection scanDirection, PointF lineStart, PointF lineMiddle, PointF lineEnd, List<LineEdgePoint> edgePoints, List<LineEdgePoint> firstPoints, List<LineEdgePoint> lastPoints, TimeSpan elapsed)
        {
            return new LineDetectionResult(true, "检测成功", frame, scanDirection, lineStart, lineMiddle, lineEnd, edgePoints, firstPoints, lastPoints, elapsed);
        }

        /// <summary>
        /// 创建失败的直线检测结果。
        /// </summary>
        /// <param name="message">失败原因。</param>
        /// <param name="frame">检测测量框。</param>
        /// <param name="scanDirection">扫描方向。</param>
        /// <param name="edgePoints">已检测到的边缘点。</param>
        /// <param name="elapsed">检测耗时。</param>
        /// <returns>失败的直线检测结果。</returns>
        public static LineDetectionResult CreateFailure(string message, LineDetectionFrame frame, LineScanDirection scanDirection, List<LineEdgePoint> edgePoints = null, TimeSpan elapsed = default(TimeSpan))
        {
            return new LineDetectionResult(false, message, frame, scanDirection, PointF.Empty, PointF.Empty, edgePoints ?? new List<LineEdgePoint>(), elapsed);
        }

        private static float CalculateAngle(PointF start, PointF end)
        {
            float angle = (float)(Math.Atan2(end.Y - start.Y, end.X - start.X) * 180d / Math.PI);
            if (angle < 0f) angle += 180f;
            if (angle >= 180f) angle -= 180f;
            return angle;
        }
    }
}
