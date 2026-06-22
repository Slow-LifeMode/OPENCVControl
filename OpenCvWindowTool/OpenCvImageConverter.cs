using OpenCvSharp;
using System.Drawing;
using System.IO;

namespace OpenCvWindowTool
{
    /// <summary>
    /// OpenCV图像与GDI图像之间的转换工具。
    /// </summary>
    internal static class OpenCvImageConverter
    {
        /// <summary>
        /// 将OpenCV Mat转换为适合WinForms绘制的Bitmap。
        /// </summary>
        /// <param name="source">OpenCV图像。</param>
        /// <returns>可绘制的Bitmap。</returns>
        public static Bitmap ToBitmap(Mat source)
        {
            using (Mat display = ConvertToDisplayMat(source))
            {
                Cv2.ImEncode(".bmp", display, out byte[] buffer);
                using (MemoryStream stream = new MemoryStream(buffer))
                using (Bitmap temp = new Bitmap(stream))
                {
                    return new Bitmap(temp);
                }
            }
        }

        /// <summary>
        /// 将不同通道数的Mat转换为BGR显示图。
        /// </summary>
        /// <param name="source">源图像。</param>
        /// <returns>BGR格式图像。</returns>
        private static Mat ConvertToDisplayMat(Mat source)
        {
            Mat result = new Mat();
            switch (source.Channels())
            {
                case 1:
                    Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);
                    break;
                case 3:
                    result = source.Clone();
                    break;
                case 4:
                    Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR);
                    break;
                default:
                    source.ConvertTo(result, MatType.CV_8UC1);
                    Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);
                    break;
            }
            return result;
        }
    }
}
