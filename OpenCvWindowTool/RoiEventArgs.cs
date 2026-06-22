using System;

namespace OpenCvWindowTool
{
    /// <summary>
    /// ROI事件参数。
    /// </summary>
    public sealed class RoiEventArgs : EventArgs
    {
        /// <summary>
        /// 创建ROI事件参数。
        /// </summary>
        /// <param name="roi">触发事件的ROI对象。</param>
        public RoiEventArgs(RoiItem roi)
        {
            Roi = roi;
        }

        /// <summary>
        /// 触发事件的ROI对象。
        /// </summary>
        public RoiItem Roi { get; }
    }
}
