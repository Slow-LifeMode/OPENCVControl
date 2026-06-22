using System.Collections.Generic;
using System.Drawing;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 直线检测结果显示适配器，负责把检测结果转换为控件叠加对象。
    /// </summary>
    public static class LineDetectionOverlayBuilder
    {
        /// <summary>
        /// 创建直线检测叠加显示对象。
        /// </summary>
        public static IEnumerable<OverlayItem> Build(LineDetectionResult result)
        {
            return Build(result, null);
        }

        public static IEnumerable<OverlayItem> Build(LineDetectionResult result, LineDetectionParams parameters)
        {
            List<OverlayItem> overlays = new List<OverlayItem>();
            if (result == null) return overlays;

            Color statusColor = result.Success ? Color.DeepSkyBlue : Color.Red;
            if (result.Frame.IsValid)
            {
                int sampleCount = parameters == null ? 0 : parameters.SampleCount;
                if (!result.Success)
                {
                    AddFrame(overlays, result.Frame, statusColor);
                }
                AddCaliperPreview(overlays, result.Frame, result.ScanDirection, sampleCount, result.Success ? Color.DeepSkyBlue : statusColor);
            }

            foreach (LineEdgePoint point in result.EdgePoints)
            {
                overlays.Add(OverlayItem.Cross(point.Point, 4f, Color.Red, 1.5f));
            }

            if (result.Success)
            {
                overlays.Add(OverlayItem.Line(result.LineStart, result.LineEnd, Color.Lime, 2f));
            }
            return overlays;
        }

        public static IEnumerable<OverlayItem> BuildPreview(LineDetectionFrame frame, LineDetectionParams parameters)
        {
            List<OverlayItem> overlays = new List<OverlayItem>();
            if (parameters == null || !frame.IsValid) return overlays;
            AddCaliperPreview(overlays, frame, parameters.ScanDirection, parameters.SampleCount, Color.DeepSkyBlue);
            return overlays;
        }

        private static void AddCaliperPreview(List<OverlayItem> overlays, LineDetectionFrame frame, LineScanDirection direction, int sampleCount, Color color)
        {
            sampleCount = System.Math.Max(2, sampleCount);
            PointF scanDir = frame.GetScanDirection(direction);
            PointF arrangeDir = frame.GetLineArrangeDirection(direction);
            float arrangeLength = frame.GetArrangeLength(direction);
            float scanLength = frame.GetScanLength(direction);
            float caliperHalfWidth = System.Math.Max(0.5f, arrangeLength / sampleCount * 0.5f);
            float arrangeStart = -arrangeLength / 2f + caliperHalfWidth;
            float arrangeEnd = arrangeLength / 2f - caliperHalfWidth;
            float arrangeStep = sampleCount == 1 ? 0f : (arrangeEnd - arrangeStart) / (sampleCount - 1);
            float halfScan = scanLength / 2f;

            for (int i = 0; i < sampleCount; i++)
            {
                float along = arrangeStart + arrangeStep * i;
                PointF center = new PointF(frame.Center.X + arrangeDir.X * along, frame.Center.Y + arrangeDir.Y * along);
                PointF start = new PointF(center.X - scanDir.X * halfScan, center.Y - scanDir.Y * halfScan);
                PointF end = new PointF(center.X + scanDir.X * halfScan, center.Y + scanDir.Y * halfScan);
                AddArrow(overlays, start, end, color, 1f, System.Math.Min(7f, halfScan * 0.25f));
            }
        }

        private static void AddFrame(List<OverlayItem> overlays, LineDetectionFrame frame, Color color)
        {
            PointF[] corners = frame.GetCorners();
            overlays.Add(OverlayItem.Line(corners[0], corners[1], color, 2f));
            overlays.Add(OverlayItem.Line(corners[1], corners[2], color, 2f));
            overlays.Add(OverlayItem.Line(corners[2], corners[3], color, 2f));
            overlays.Add(OverlayItem.Line(corners[3], corners[0], color, 2f));
        }

        private static void AddArrow(List<OverlayItem> overlays, PointF start, PointF end, Color color)
        {
            AddArrow(overlays, start, end, color, 2f, 12f);
        }

        private static void AddArrow(List<OverlayItem> overlays, PointF start, PointF end, Color color, float lineWidth, float arrowLength)
        {
            overlays.Add(OverlayItem.Line(start, end, color, lineWidth));
            overlays.Add(OverlayItem.Line(end, GetArrowWing(start, end, 30f, arrowLength), color, lineWidth));
            overlays.Add(OverlayItem.Line(end, GetArrowWing(start, end, -30f, arrowLength), color, lineWidth));
        }

        private static PointF GetArrowWing(PointF start, PointF end, float angle, float length)
        {
            float dx = start.X - end.X;
            float dy = start.Y - end.Y;
            float scale = length / System.Math.Max(0.001f, (float)System.Math.Sqrt(dx * dx + dy * dy));
            dx *= scale;
            dy *= scale;
            double radians = angle * System.Math.PI / 180d;
            float cos = (float)System.Math.Cos(radians);
            float sin = (float)System.Math.Sin(radians);
            return new PointF(end.X + dx * cos - dy * sin, end.Y + dx * sin + dy * cos);
        }
    }
}
