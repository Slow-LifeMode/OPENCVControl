using System.Windows.Forms;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 图像显示画布，统一开启双缓冲以减少缩放和拖动时的闪烁。
    /// </summary>
    internal sealed class BufferedImagePanel : Panel
    {
        /// <summary>
        /// 初始化双缓冲画布。
        /// </summary>
        public BufferedImagePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        /// <summary>
        /// 跳过默认背景擦除，避免绘制图像前先清屏导致闪烁。
        /// </summary>
        /// <param name="e">绘制参数。</param>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
