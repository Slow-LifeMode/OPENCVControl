using System;
using System.Drawing;

namespace OpenCvWindowTool
{
    public enum OverlayShape
    {
        Rectangle,
        Circle,
        Line,
        Cross,
        Text
    }

    public sealed class OverlayItem
    {
        public OverlayShape Shape { get; private set; }
        public RectangleF Bounds { get; private set; }
        public PointF Point1 { get; private set; }
        public PointF Point2 { get; private set; }
        public string Text { get; private set; }
        public Color Color { get; set; }
        public float LineWidth { get; set; }
        public Font Font { get; set; }

        private OverlayItem()
        {
            Color = Color.Lime;
            LineWidth = 1f;
            Font = new Font("Microsoft YaHei UI", 10f);
        }

        public static OverlayItem CreateRectangle(RectangleF rectangle, Color color, float lineWidth = 1f)
        {
            return new OverlayItem { Shape = OverlayShape.Rectangle, Bounds = rectangle, Color = color, LineWidth = lineWidth };
        }

        public static OverlayItem Circle(PointF center, float radius, Color color, float lineWidth = 1f)
        {
            return new OverlayItem
            {
                Shape = OverlayShape.Circle,
                Bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f),
                Color = color,
                LineWidth = lineWidth
            };
        }

        public static OverlayItem Line(PointF start, PointF end, Color color, float lineWidth = 1f)
        {
            return new OverlayItem { Shape = OverlayShape.Line, Point1 = start, Point2 = end, Color = color, LineWidth = lineWidth };
        }

        public static OverlayItem Cross(PointF center, float size, Color color, float lineWidth = 1f)
        {
            return new OverlayItem
            {
                Shape = OverlayShape.Cross,
                Point1 = center,
                Point2 = new PointF(size, size),
                Color = color,
                LineWidth = lineWidth
            };
        }

        public static OverlayItem TextItem(string text, PointF location, Color color, Font font = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new OverlayItem { Shape = OverlayShape.Text, Text = text, Point1 = location, Color = color, Font = font ?? new Font("Microsoft YaHei UI", 10f) };
        }
    }
}
