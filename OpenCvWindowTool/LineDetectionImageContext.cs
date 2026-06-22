using OpenCvSharp;
using System;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 保存直线检测可复用的图像预处理数据。
    /// </summary>
    public sealed class LineDetectionImageContext : IDisposable
    {
        /// <summary>
        /// 初始化直线检测图像上下文。
        /// </summary>
        /// <param name="grayImage">8位单通道灰度图。</param>
        public LineDetectionImageContext(Mat grayImage)
        {
            if (grayImage == null || grayImage.Empty()) throw new ArgumentNullException(nameof(grayImage));

            GrayImage = grayImage.Clone();
            Width = GrayImage.Width;
            Height = GrayImage.Height;
            GrayImage.GetArray(out byte[] pixels);
            GrayPixels = pixels;
        }

        /// <summary>
        /// 获取图像宽度。
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// 获取图像高度。
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// 获取按行存储的灰度像素。
        /// </summary>
        public byte[] GrayPixels { get; private set; }

        /// <summary>
        /// 获取灰度图缓存。
        /// </summary>
        public Mat GrayImage { get; private set; }

        /// <summary>
        /// 从OpenCV图像创建直线检测上下文。
        /// </summary>
        /// <param name="image">源图像。</param>
        /// <returns>直线检测图像上下文；源图像无效时返回null。</returns>
        public static LineDetectionImageContext FromImage(Mat image)
        {
            if (image == null || image.Empty()) return null;

            using (Mat gray = ToGray(image))
            {
                return new LineDetectionImageContext(gray);
            }
        }

        /// <summary>
        /// 将源图像转换为8位单通道灰度图。
        /// </summary>
        /// <param name="image">源图像。</param>
        /// <returns>8位单通道灰度图。</returns>
        public static Mat ToGray(Mat image)
        {
            if (image == null || image.Empty()) return new Mat();

            Mat gray = new Mat();
            if (image.Channels() == 1)
            {
                image.ConvertTo(gray, MatType.CV_8U);
            }
            else if (image.Channels() == 3)
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            }
            else if (image.Channels() == 4)
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGRA2GRAY);
            }
            else
            {
                image.ConvertTo(gray, MatType.CV_8U);
            }

            return gray;
        }

        /// <summary>
        /// 释放灰度图缓存。
        /// </summary>
        public void Dispose()
        {
            GrayImage?.Dispose();
            GrayImage = null;
            GrayPixels = null;
        }
    }
}
