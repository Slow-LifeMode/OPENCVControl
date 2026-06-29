using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace OpenCvWindowTool
{
    public sealed class LineDetectionOperator
    {
        /// <summary>
        /// 从源图像执行直线检测。
        /// </summary>
        /// <param name="image">源图像。</param>
        /// <param name="roi">检测ROI。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>直线检测结果。</returns>
        public LineDetectionResult Detect(Mat image, RoiItem roi, LineDetectionParams parameters)
        {
            using (LineDetectionImageContext context = LineDetectionImageContext.FromImage(image))
            {
                return Detect(context, roi, parameters);
            }
        }

        /// <summary>
        /// 从已预处理的图像上下文执行直线检测。
        /// </summary>
        /// <param name="context">直线检测图像上下文。</param>
        /// <param name="roi">检测ROI。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>直线检测结果。</returns>
        public LineDetectionResult Detect(LineDetectionImageContext context, RoiItem roi, LineDetectionParams parameters)
        {
            LineDetectionParams actualParams = NormalizeParams(parameters);
            if (context == null || context.GrayImage == null || context.GrayImage.Empty() || context.GrayPixels == null)
            {
                return LineDetectionResult.CreateFailure("当前没有可检测的图像。", default(LineDetectionFrame), actualParams.ScanDirection);
            }
            if (roi == null)
            {
                return LineDetectionResult.CreateFailure("请先选择普通矩形ROI或带角度矩形ROI。", default(LineDetectionFrame), actualParams.ScanDirection);
            }
            if (!roi.CanDetectLine())
            {
                return LineDetectionResult.CreateFailure("直线检测只支持普通矩形ROI和带角度矩形ROI。", default(LineDetectionFrame), actualParams.ScanDirection);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            LineDetectionFrame frame = roi.ToLineDetectionFrame();
            List<LineEdgePoint> edgePoints = CollectEdgePoints(context, frame, actualParams);
            if (edgePoints.Count < 2)
            {
                return LineDetectionResult.CreateFailure("有效检测点不足，无法拟合直线。", frame, actualParams.ScanDirection, edgePoints, stopwatch.Elapsed);
            }

            List<LineEdgePoint> fittingPoints = RejectOutliers(edgePoints, actualParams);
            if (fittingPoints.Count < 2)
            {
                fittingPoints = edgePoints;
            }

            PointF[] line = FitLine(frame, actualParams, fittingPoints);
            return LineDetectionResult.CreateSuccess(frame, actualParams.ScanDirection, line[0], line[1], edgePoints, stopwatch.Elapsed);
        }

        private static LineDetectionParams NormalizeParams(LineDetectionParams parameters)
        {
            LineDetectionParams source = parameters ?? new LineDetectionParams();
            LineDetectionParams result = new LineDetectionParams
            {
                EdgeThreshold = Math.Max(0f, source.EdgeThreshold),
                SampleCount = Math.Max(2, source.SampleCount),
                SampleStep = Math.Max(0.5f, source.SampleStep),
                SmoothSize = MakeOdd(Math.Max(1, source.SmoothSize)),
                EdgeWidth = Math.Max(1, source.EdgeWidth),
                ProjectionWidth = MakeOdd(Math.Max(1, source.ProjectionWidth)),
                RejectRatio = Math.Min(99, Math.Max(0, source.RejectRatio)),
                RejectDistance = Math.Min(20, Math.Max(0, source.RejectDistance)),
                ShowSearchLines = source.ShowSearchLines,
                EdgePolarity = source.EdgePolarity,
                StrengthType = source.StrengthType,
                SelectionMode = source.SelectionMode,
                ScanDirection = source.ScanDirection,
                FitMode = source.FitMode
            };
            return result;
        }

        /// <summary>
        /// 把输入数值调整为奇数。
        /// </summary>
        /// <param name="value">输入数值。</param>
        /// <returns>奇数数值。</returns>
        private static int MakeOdd(int value)
        {
            return value % 2 == 0 ? value + 1 : value;
        }

        private static List<LineEdgePoint> CollectEdgePoints(LineDetectionImageContext context, LineDetectionFrame frame, LineDetectionParams parameters)
        {
            //扫描方向
            PointF scanDir = frame.GetScanDirection(parameters.ScanDirection);
            //排列方向
            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            //ROI长
            float arrangeLength = frame.GetArrangeLength(parameters.ScanDirection);
            //ROI宽
            float scanLength = frame.GetScanLength(parameters.ScanDirection);
            //切割之后每个扫描卡尺宽度的一半
            float arrangeStart = -arrangeLength / 2f;
            //第一个卡尺相对中心点的起始偏移量
            //每个卡尺之间的固定步长
            float arrangeStep = arrangeLength / parameters.SampleCount;

            // 优化：预计算ROI区域的Sobel梯度，只计算一次
            List<List<CaliperCandidate>> candidateGroups = new List<List<CaliperCandidate>>(parameters.SampleCount);
                
            for (int i = 0; i < parameters.SampleCount; i++)
            {
                float along = arrangeStart + arrangeStep * (i + 0.5f);
                PointF center = new PointF(frame.Center.X + arrangeDir.X * along, frame.Center.Y + arrangeDir.Y * along);
                List<CaliperCandidate> candidates = DetectOneSearchLine(context, center, scanDir, arrangeDir, scanLength, i, parameters);
                    
                if (candidates.Count > 10)
                {
                    switch (parameters.SelectionMode)
                    {
                        case LineSelectionMode.First:
                            candidates = candidates.OrderBy(c => c.Offset).Take(3).ToList();
                            break;
                        case LineSelectionMode.Last:
                            candidates = candidates.OrderByDescending(c => c.Offset).Take(3).ToList();
                            break;
                        default:
                            candidates = candidates.OrderByDescending(c => c.Strength).Take(3).ToList();
                            break;
                    }
                }
                candidateGroups.Add(candidates);
            }

            return parameters.SelectionMode == LineSelectionMode.Strongest
                ? SelectGloballyConsistentPointsOptimized(candidateGroups, frame, parameters)
                : SelectOrderedConsistentPointsOptimized(candidateGroups, frame, parameters);
        }

        /// <summary>
        /// 在单条搜索线上提取候选边缘点。
        /// </summary>
        /// <param name="context">灰度图上下文。</param>
        /// <param name="center">搜索线中心点。</param>
        /// <param name="scanDir">扫描方向。</param>
        /// <param name="widthDir">投影宽度方向。</param>
        /// <param name="scanLength">扫描长度。</param>
        /// <param name="lineIndex">搜索线编号，从0开始。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>候选边缘点集合。</returns>
        private static List<CaliperCandidate> DetectOneSearchLine(LineDetectionImageContext context, PointF center, PointF scanDir, PointF widthDir, float scanLength, int lineIndex, LineDetectionParams parameters)
        {
            int sampleCount = Math.Max(5, (int)Math.Ceiling(scanLength / parameters.SampleStep) + 1);
            float sampleStep = scanLength / Math.Max(1, sampleCount - 1);
            float scanStart = -scanLength / 2f;
            float[] profile = new float[sampleCount];
            float[] offsets = new float[sampleCount];
            bool[] valid = new bool[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float offset = scanStart + sampleStep * i;
                PointF point = new PointF(center.X + scanDir.X * offset, center.Y + scanDir.Y * offset);
                offsets[i] = offset;
                if (TryReadProjectedGray(context, point, widthDir, parameters.ProjectionWidth, out float gray))
                {
                    profile[i] = gray;
                    valid[i] = true;
                }
            }

            FillInvalidSamples(profile, valid);
            if (parameters.SmoothSize > 1)
            {
                Smooth(profile, parameters.SmoothSize);
            }

            return FindCandidates(profile, offsets, valid, center, scanDir, lineIndex, parameters);
        }

        /// <summary>
        /// 读取指定点附近投影宽度内的平均灰度。
        /// </summary>
        /// <param name="context">灰度图上下文。</param>
        /// <param name="center">投影中心点。</param>
        /// <param name="widthDir">投影宽度方向。</param>
        /// <param name="projectionWidth">投影宽度。</param>
        /// <param name="gray">平均灰度。</param>
        /// <returns>成功读取至少一个像素时返回true。</returns>
        private static bool TryReadProjectedGray(LineDetectionImageContext context, PointF center, PointF widthDir, int projectionWidth, out float gray)
        {
            int half = projectionWidth / 2;
            float sum = 0f;
            int count = 0;

            for (int i = -half; i <= half; i++)
            {
                float px = center.X + widthDir.X * i;
                float py = center.Y + widthDir.Y * i;
                if (TryReadGray(context, px, py, out float value))
                {
                    sum += value;
                    count++;
                }
            }

            gray = count == 0 ? 0f : sum / count;
            return count > 0;
        }

        /// <summary>
        /// 双线性读取灰度值。
        /// </summary>
        /// <param name="context">灰度图上下文。</param>
        /// <param name="x">图像X坐标。</param>
        /// <param name="y">图像Y坐标。</param>
        /// <param name="value">灰度值。</param>
        /// <returns>坐标在图像内时返回true。</returns>
        private static bool TryReadGray(LineDetectionImageContext context, float x, float y, out float value)
        {
            value = 0f;
            if (x < 0f || y < 0f || x > context.Width - 1 || y > context.Height - 1) return false;

            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = Math.Min(context.Width - 1, x0 + 1);
            int y1 = Math.Min(context.Height - 1, y0 + 1);
            float fx = x - x0;
            float fy = y - y0;
            float v00 = ReadPixel(context, x0, y0);
            float v10 = ReadPixel(context, x1, y0);
            float v01 = ReadPixel(context, x0, y1);
            float v11 = ReadPixel(context, x1, y1);
            float top = v00 + (v10 - v00) * fx;
            float bottom = v01 + (v11 - v01) * fx;
            value = top + (bottom - top) * fy;
            return true;
        }

        /// <summary>
        /// 读取单个像素灰度。
        /// </summary>
        /// <param name="context">灰度图上下文。</param>
        /// <param name="x">像素X坐标。</param>
        /// <param name="y">像素Y坐标。</param>
        /// <returns>灰度值。</returns>
        private static byte ReadPixel(LineDetectionImageContext context, int x, int y)
        {
            return context.GrayPixels[y * context.Width + x];
        }

        /// <summary>
        /// 用最近有效样本补齐无效采样点。
        /// </summary>
        /// <param name="profile">灰度剖面。</param>
        /// <param name="valid">有效标记。</param>
        private static void FillInvalidSamples(float[] profile, bool[] valid)
        {
            float last = 0f;
            bool hasLast = false;
            for (int i = 0; i < profile.Length; i++)
            {
                if (valid[i])
                {
                    last = profile[i];
                    hasLast = true;
                }
                else if (hasLast)
                {
                    profile[i] = last;
                }
            }

            float next = 0f;
            bool hasNext = false;
            for (int i = profile.Length - 1; i >= 0; i--)
            {
                if (valid[i])
                {
                    next = profile[i];
                    hasNext = true;
                }
                else if (hasNext)
                {
                    profile[i] = next;
                    valid[i] = true;
                }
            }
        }

        /// <summary>
        /// 对一维剖面执行滑动平均平滑。
        /// </summary>
        /// <param name="profile">灰度剖面。</param>
        /// <param name="size">窗口大小。</param>
        private static void Smooth(float[] profile, int size)
        {
            if (profile.Length == 0 || size <= 1) return;

            int half = size / 2;
            float[] source = (float[])profile.Clone();
            float[] prefix = new float[source.Length + 1];
            for (int i = 0; i < source.Length; i++)
            {
                prefix[i + 1] = prefix[i] + source[i];
            }

            for (int i = 0; i < source.Length; i++)
            {
                int start = Math.Max(0, i - half);
                int end = Math.Min(source.Length, i + half + 1);
                profile[i] = (prefix[end] - prefix[start]) / Math.Max(1, end - start);
            }
        }

        /// <summary>
        /// 从一维剖面中查找候选边缘。
        /// </summary>
        /// <param name="profile">灰度剖面。</param>
        /// <param name="offsets">扫描偏移。</param>
        /// <param name="valid">有效标记。</param>
        /// <param name="center">搜索线中心点。</param>
        /// <param name="scanDir">扫描方向。</param>
        /// <param name="lineIndex">搜索线编号。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>候选边缘集合。</returns>
        private static List<CaliperCandidate> FindCandidates(float[] profile, float[] offsets, bool[] valid, PointF center, PointF scanDir, int lineIndex, LineDetectionParams parameters)
        {
            List<CaliperCandidate> candidates = new List<CaliperCandidate>();
            CaliperCandidate fallback = CaliperCandidate.Invalid;
            int width = Math.Max(1, parameters.EdgeWidth);
            float[] gradients = BuildCaliperGradients(profile, valid, width);
            for (int i = width; i < profile.Length - width; i++)
            {
                if (!valid[i]) continue;

                float gradient = gradients[i];
                float strength = Math.Abs(gradient);
                if (strength < parameters.EdgeThreshold) continue;
                if (!MatchPolarity(gradient, parameters.EdgePolarity)) continue;

                float offset = offsets[i];
                PointF point = new PointF(center.X + scanDir.X * offset, center.Y + scanDir.Y * offset);
                CaliperCandidate candidate = new CaliperCandidate(point, offset, strength, lineIndex);
                if (!fallback.IsValid || candidate.Strength > fallback.Strength)
                {
                    fallback = candidate;
                }
                if (IsLocalGradientPeak(gradients, i, width))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0 && fallback.IsValid)
            {
                candidates.Add(fallback);
            }

            candidates.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return MergeNearbyCandidates(candidates, parameters.EdgeWidth);
        }

        /// <summary>
        /// 按卡尺边缘宽度计算一维灰度剖面的左右窗口梯度。
        /// </summary>
        /// <param name="profile">灰度剖面。</param>
        /// <param name="valid">有效采样标记。</param>
        /// <param name="edgeWidth">边缘宽度。</param>
        /// <returns>每个采样位置的梯度。</returns>
        private static float[] BuildCaliperGradients(float[] profile, bool[] valid, int edgeWidth)
        {
            float[] gradients = new float[profile.Length];
            if (profile.Length == 0) return gradients;

            float[] prefix = new float[profile.Length + 1];
            int[] validPrefix = new int[profile.Length + 1];
            for (int i = 0; i < profile.Length; i++)
            {
                prefix[i + 1] = prefix[i] + profile[i];
                validPrefix[i + 1] = validPrefix[i] + (valid[i] ? 1 : 0);
            }

            int width = Math.Max(1, edgeWidth);
            for (int i = width; i < profile.Length - width; i++)
            {
                int leftStart = i - width;
                int leftEnd = i;
                int rightStart = i + 1;
                int rightEnd = i + width + 1;
                int leftCount = validPrefix[leftEnd] - validPrefix[leftStart];
                int rightCount = validPrefix[rightEnd] - validPrefix[rightStart];
                if (leftCount == 0 || rightCount == 0) continue;

                float leftMean = (prefix[leftEnd] - prefix[leftStart]) / leftCount;
                float rightMean = (prefix[rightEnd] - prefix[rightStart]) / rightCount;
                gradients[i] = rightMean - leftMean;
            }

            return gradients;
        }

        /// <summary>
        /// 判断指定梯度点是否为局部峰值。
        /// </summary>
        /// <param name="gradients">梯度数组。</param>
        /// <param name="index">采样索引。</param>
        /// <param name="edgeWidth">边缘宽度。</param>
        /// <returns>是局部峰值时返回true。</returns>
        private static bool IsLocalGradientPeak(float[] gradients, int index, int edgeWidth)
        {
            float current = Math.Abs(gradients[index]);
            int radius = Math.Max(1, edgeWidth / 2);
            int start = Math.Max(0, index - radius);
            int end = Math.Min(gradients.Length - 1, index + radius);
            for (int i = start; i <= end; i++)
            {
                if (i == index) continue;
                if (Math.Abs(gradients[i]) > current) return false;
            }

            return true;
        }

        /// <summary>
        /// 合并同一搜索线中距离过近的候选边缘。
        /// </summary>
        /// <param name="candidates">候选边缘集合。</param>
        /// <param name="minimumDistance">最小距离。</param>
        /// <returns>合并后的候选边缘集合。</returns>
        private static List<CaliperCandidate> MergeNearbyCandidates(List<CaliperCandidate> candidates, int minimumDistance)
        {
            if (candidates.Count <= 1) return candidates;

            List<CaliperCandidate> result = new List<CaliperCandidate>();
            CaliperCandidate current = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                CaliperCandidate candidate = candidates[i];
                if (Math.Abs(candidate.Offset - current.Offset) <= minimumDistance)
                {
                    if (candidate.Strength > current.Strength) current = candidate;
                }
                else
                {
                    result.Add(current);
                    current = candidate;
                }
            }
            result.Add(current);
            return result;
        }

        /// <summary>
        /// 优化版本的单个卡尺检测，使用直接内存访问和预计算梯度
        /// </summary>
        private static List<CaliperCandidate> DetectOneCaliperOptimized(Mat gray, Mat gradMat, PointF center, PointF scanDir, PointF widthDir, float scanLength, float halfWidth, int caliperIndex, LineDetectionParams parameters, Rect roiRect)
        {
            // 优化：减少采样点数，使用整数步长
            int scanCount = Math.Max(5, (int)(scanLength / parameters.SampleStep));
            int widthCount = Math.Max(1, (int)((halfWidth * 2f) / parameters.SampleStep));
            float scanStep = scanLength / scanCount;
            float widthStep = (halfWidth * 2f) / widthCount;
            float scanStart = -scanLength / 2f;
            float widthStart = -halfWidth;

            // 优化：使用数组代替List，减少内存分配
            float[] grayProfile = new float[scanCount];
            float[] gradProfile = new float[scanCount];
            int[] validCount = new int[scanCount];

            for (int scanIndex = 0; scanIndex < scanCount; scanIndex++)
            {
                float scanOffset = scanStart + scanIndex * scanStep;
                float cx = center.X + scanDir.X * scanOffset;
                float cy = center.Y + scanDir.Y * scanOffset;
                double graySum = 0;
                double gradSum = 0;
                int count = 0;

                for (int widthIndex = 0; widthIndex < widthCount; widthIndex++)
                {
                    float widthOffset = widthStart + widthIndex * widthStep;
                    float px = cx + widthDir.X * widthOffset;
                    float py = cy + widthDir.Y * widthOffset;

                    int ix = (int)px;
                    int iy = (int)py;
                    if (ix < 0 || ix >= gray.Width - 1 || iy < 0 || iy >= gray.Height - 1)
                        continue;

                    // 优化：直接内存访问，避免双线性插值
                    graySum += gray.Get<byte>(iy, ix);

                    if (gradMat != null)
                    {
                        int gx = ix - roiRect.X;
                        int gy = iy - roiRect.Y;
                        if (gx >= 0 && gx < gradMat.Width && gy >= 0 && gy < gradMat.Height)
                        {
                            gradSum += gradMat.Get<float>(gy, gx);
                        }
                    }
                    count++;
                }

                if (count > 0)
                {
                    grayProfile[scanIndex] = (float)(graySum / count);
                    gradProfile[scanIndex] = (float)(gradSum / count);
                    validCount[scanIndex] = count;
                }
                else
                {
                    validCount[scanIndex] = 0;
                }
            }

            // 优化：只处理有效采样点
            int validScanCount = 0;
            for (int i = 0; i < scanCount; i++)
            {
                if (validCount[i] > 0) validScanCount++;
            }

            if (validScanCount < 3) return new List<CaliperCandidate>();

            // 优化：分离有效的采样点
            float[] validGray = new float[validScanCount];
            float[] validGrad = new float[validScanCount];
            float[] validOffsets = new float[validScanCount];
            int idx = 0;
            for (int i = 0; i < scanCount; i++)
            {
                if (validCount[i] > 0)
                {
                    validGray[idx] = grayProfile[i];
                    validGrad[idx] = gradProfile[i];
                    validOffsets[idx] = scanStart + i * scanStep;
                    idx++;
                }
            }

            // 优化：使用简单的均值平滑
            if (parameters.SmoothSize > 1)
            {
                SmoothOptimized(validGray, validGrad, parameters.SmoothSize);
            }

            return FindCandidatesOptimized(validGray, validGrad, validOffsets, center, scanDir, caliperIndex, parameters);
        }

        /// <summary>
        /// 优化的平滑算法，使用前缀和实现O(n)复杂度
        /// </summary>
        private static void SmoothOptimized(float[] gray, float[] grad, int size)
        {
            if (size <= 1 || gray.Length < size) return;

            int half = size / 2;
            int n = gray.Length;

            // 使用前缀和快速计算滑动窗口平均
            float[] grayPrefix = new float[n + 1];
            float[] gradPrefix = new float[n + 1];
            
            for (int i = 0; i < n; i++)
            {
                grayPrefix[i + 1] = grayPrefix[i] + gray[i];
                gradPrefix[i + 1] = gradPrefix[i] + grad[i];
            }

            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - half);
                int end = Math.Min(n, i + half + 1);
                int count = end - start;
                gray[i] = (grayPrefix[end] - grayPrefix[start]) / count;
                grad[i] = (gradPrefix[end] - gradPrefix[start]) / count;
            }
        }

        /// <summary>
        /// 优化的候选点检测，直接操作数组
        /// </summary>
        private static List<CaliperCandidate> FindCandidatesOptimized(float[] gray, float[] grad, float[] offsets, PointF center, PointF scanDir, int caliperIndex, LineDetectionParams parameters)
        {
            List<CaliperCandidate> candidates = new List<CaliperCandidate>(4);
            int n = gray.Length;

            for (int i = 1; i < n - 1; i++)
            {
                float gradient = parameters.StrengthType == LineEdgeStrengthType.Sobel 
                    ? grad[i] 
                    : (gray[i + 1] - gray[i - 1]) / (offsets[i + 1] - offsets[i - 1] + 0.0001f);
                
                float strength = Math.Abs(gradient);
                if (strength < parameters.EdgeThreshold) continue;

                if (!MatchPolarity(gradient, parameters.EdgePolarity)) continue;

                float prevGrad = i > 1 ? Math.Abs(parameters.StrengthType == LineEdgeStrengthType.Sobel 
                    ? grad[i - 1] 
                    : (gray[i] - gray[i - 2]) / (offsets[i] - offsets[i - 2] + 0.0001f)) : 0f;
                float nextGrad = i < n - 2 ? Math.Abs(parameters.StrengthType == LineEdgeStrengthType.Sobel 
                    ? grad[i + 1] 
                    : (gray[i + 2] - gray[i]) / (offsets[i + 2] - offsets[i] + 0.0001f)) : 0f;

                if (strength < prevGrad || strength < nextGrad) continue;

                // 优化：简化边缘点精化
                float offset = offsets[i];
                PointF point = new PointF(center.X + scanDir.X * offset, center.Y + scanDir.Y * offset);
                candidates.Add(new CaliperCandidate(point, offset, strength, caliperIndex));
            }

            return candidates;
        }

        /// <summary>
        /// 优化的全局一致性点选择，减少假设生成数量
        /// </summary>
        private static List<LineEdgePoint> SelectGloballyConsistentPointsOptimized(List<List<CaliperCandidate>> candidateGroups, LineDetectionFrame frame, LineDetectionParams parameters)
        {
            int totalCandidates = candidateGroups.Sum(g => g.Count);
            if (totalCandidates < 2)
            {
                return SelectFallbackPointsOptimized(candidateGroups, parameters);
            }

            // 优化：只从每个卡尺取最强的候选点生成假设
            List<CaliperCandidate> seedCandidates = candidateGroups
                .Where(g => g.Count > 0)
                .Select(g => g.OrderByDescending(c => c.Strength).First())
                .ToList();

            if (seedCandidates.Count < 2)
            {
                return SelectFallbackPointsOptimized(candidateGroups, parameters);
            }

            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            float tolerance = Math.Max(2.0f, Math.Min(8.0f, frame.GetScanLength(parameters.ScanDirection) * 0.04f));

            // 优化：使用随机采样或均匀采样代替全组合
            LineHypothesis best = FindBestLineHypothesisOptimized(seedCandidates, candidateGroups, arrangeDir, tolerance, parameters);
            
            if (!best.IsValid)
            {
                // 如果没有找到好的假设，尝试使用所有候选点
                List<CaliperCandidate> allCandidates = candidateGroups.SelectMany(g => g).ToList();
                if (allCandidates.Count >= 2)
                {
                    best = FindBestLineHypothesisOptimized(allCandidates.Take(8).ToList(), candidateGroups, arrangeDir, tolerance, parameters);
                }
                
                if (!best.IsValid)
                {
                    return SelectFallbackPointsOptimized(candidateGroups, parameters);
                }
            }

            List<LineEdgePoint> selected = SelectInliersOptimized(candidateGroups, best, tolerance * 1.75f, parameters);
            return selected.Count >= 2 ? selected : SelectFallbackPointsOptimized(candidateGroups, parameters);
        }

        /// <summary>
        /// 按第一条或最后一条语义选择候选点，并只保留同一条直线上的一致点。
        /// </summary>
        /// <param name="candidateGroups">每条卡尺上的候选边缘点集合。</param>
        /// <param name="frame">直线检测ROI测量框。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <returns>符合当前首尾选择模式且几何一致的边缘点集合。</returns>
        private static List<LineEdgePoint> SelectOrderedConsistentPointsOptimized(List<List<CaliperCandidate>> candidateGroups, LineDetectionFrame frame, LineDetectionParams parameters)
        {
            List<List<CaliperCandidate>> validGroups = candidateGroups
                .Where(g => g.Count > 0)
                .ToList();

            if (validGroups.Count < 2)
            {
                return ToLineEdgePoints(validGroups.Select(g => SelectCandidateByMode(g, parameters.SelectionMode)).ToList());
            }

            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            float tolerance = Math.Max(2.0f, Math.Min(8.0f, frame.GetScanLength(parameters.ScanDirection) * 0.04f));
            LineHypothesis orderedBest = FindBestOrderedLineHypothesis(validGroups, arrangeDir, tolerance, parameters);
            if (orderedBest.IsValid)
            {
                List<LineEdgePoint> orderedSelected = SelectInliersOptimized(validGroups, orderedBest, tolerance * 1.75f, parameters);
                if (orderedSelected.Count >= 2)
                {
                    return orderedSelected;
                }
            }

            List<CaliperCandidate> orderedCandidates = validGroups
                .Select(g => SelectCandidateByMode(g, parameters.SelectionMode))
                .ToList();

            if (orderedCandidates.Count < 2)
            {
                return ToLineEdgePoints(orderedCandidates);
            }

            List<List<CaliperCandidate>> orderedGroups = orderedCandidates
                .Select(c => new List<CaliperCandidate> { c })
                .ToList();
            LineHypothesis best = FindBestLineHypothesisByAllPairs(orderedCandidates, orderedGroups, arrangeDir, tolerance, parameters);
            if (!best.IsValid)
            {
                return ToLineEdgePoints(orderedCandidates);
            }

            List<LineEdgePoint> selected = SelectInliersOptimized(orderedGroups, best, tolerance * 1.75f, parameters);
            return selected.Count >= 2 ? selected : ToLineEdgePoints(orderedCandidates);
        }

        /// <summary>
        /// 在所有候选点中查找首条或末条几何连续的边缘线。
        /// </summary>
        /// <param name="groups">每条卡尺上的候选边缘点集合。</param>
        /// <param name="arrangeDir">卡尺排列方向。</param>
        /// <param name="tolerance">点到直线的允许距离。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <returns>符合首尾语义的最佳直线假设。</returns>
        private static LineHypothesis FindBestOrderedLineHypothesis(List<List<CaliperCandidate>> groups, PointF arrangeDir, float tolerance, LineDetectionParams parameters)
        {
            List<CaliperCandidate> candidates = groups.SelectMany(g => g).ToList();
            if (candidates.Count < 2)
            {
                return LineHypothesis.Invalid;
            }

            int minSupport = Math.Max(4, (int)Math.Ceiling(groups.Count * 0.50d));
            int minContinuous = Math.Max(3, (int)Math.Ceiling(groups.Count * 0.20d));
            LineHypothesis best = LineHypothesis.Invalid;
            float bestOffset = 0f;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < candidates.Count - 1; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (candidates[i].CaliperIndex == candidates[j].CaliperIndex) continue;

                    LineHypothesis hypothesis = LineHypothesis.FromPoints(candidates[i].Point, candidates[j].Point);
                    if (!hypothesis.IsValid) continue;

                    float orientation = Math.Abs(Dot(hypothesis.Direction, arrangeDir));
                    if (orientation < 0.9f) continue;

                    float averageOffset;
                    float score;
                    if (!TryScoreOrderedHypothesis(hypothesis, groups, tolerance, minSupport, minContinuous, orientation, out averageOffset, out score))
                    {
                        continue;
                    }

                    if (!best.IsValid || IsBetterOrderedHypothesis(averageOffset, score, bestOffset, bestScore, parameters.SelectionMode))
                    {
                        best = hypothesis.WithScore(score);
                        bestOffset = averageOffset;
                        bestScore = score;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// 计算候选直线的连续支持度和扫描方向位置。
        /// </summary>
        private static bool TryScoreOrderedHypothesis(LineHypothesis hypothesis, List<List<CaliperCandidate>> groups, float tolerance, int minSupport, int minContinuous, float orientation, out float averageOffset, out float score)
        {
            int support = 0;
            int continuous = 0;
            int bestContinuous = 0;
            float offsetSum = 0f;
            score = orientation;

            foreach (List<CaliperCandidate> group in groups)
            {
                CaliperCandidate bestCandidate = CaliperCandidate.Invalid;
                float bestDistance = float.MaxValue;

                foreach (CaliperCandidate candidate in group)
                {
                    float dx = candidate.Point.X - hypothesis.Point.X;
                    float dy = candidate.Point.Y - hypothesis.Point.Y;
                    float distance = Math.Abs(dx * hypothesis.Normal.X + dy * hypothesis.Normal.Y);
                    if (distance <= tolerance && (distance < bestDistance || !bestCandidate.IsValid || candidate.Strength > bestCandidate.Strength))
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate.IsValid)
                {
                    support++;
                    continuous++;
                    if (continuous > bestContinuous) bestContinuous = continuous;
                    offsetSum += bestCandidate.Offset;
                    score += 1.0f + (1.0f - bestDistance / tolerance) * 0.5f;
                }
                else
                {
                    continuous = 0;
                }
            }

            averageOffset = support == 0 ? 0f : offsetSum / support;
            score += bestContinuous * 0.2f;
            return support >= minSupport && bestContinuous >= minContinuous;
        }

        /// <summary>
        /// 按扫描顺序比较首条或末条候选直线。
        /// </summary>
        private static bool IsBetterOrderedHypothesis(float offset, float score, float bestOffset, float bestScore, LineSelectionMode selectionMode)
        {
            const float offsetTolerance = 0.5f;
            if (selectionMode == LineSelectionMode.First)
            {
                if (offset < bestOffset - offsetTolerance) return true;
                if (offset > bestOffset + offsetTolerance) return false;
            }
            else
            {
                if (offset > bestOffset + offsetTolerance) return true;
                if (offset < bestOffset - offsetTolerance) return false;
            }

            return score > bestScore;
        }

        /// <summary>
        /// 从全部候选点对中查找得分最高的直线假设。
        /// </summary>
        /// <param name="candidates">用于生成假设的候选点。</param>
        /// <param name="groups">用于评分的候选点分组。</param>
        /// <param name="arrangeDir">卡尺排列方向。</param>
        /// <param name="tolerance">点到直线的允许距离。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <returns>得分最高的直线假设；没有有效假设时返回无效假设。</returns>
        private static LineHypothesis FindBestLineHypothesisByAllPairs(List<CaliperCandidate> candidates, List<List<CaliperCandidate>> groups, PointF arrangeDir, float tolerance, LineDetectionParams parameters)
        {
            LineHypothesis best = LineHypothesis.Invalid;
            int count = candidates.Count;

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (candidates[i].CaliperIndex == candidates[j].CaliperIndex) continue;

                    LineHypothesis hypothesis = LineHypothesis.FromPoints(candidates[i].Point, candidates[j].Point);
                    if (!hypothesis.IsValid) continue;

                    float orientation = Math.Abs(Dot(hypothesis.Direction, arrangeDir));
                    if (orientation < 0.5f) continue;

                    float score = ScoreHypothesisOptimized(hypothesis, groups, tolerance, parameters);
                    score += orientation;
                    if (score > best.Score)
                    {
                        best = hypothesis.WithScore(score);
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// 优化的最佳直线假设查找，限制候选点对数量
        /// </summary>
        private static LineHypothesis FindBestLineHypothesisOptimized(List<CaliperCandidate> candidates, List<List<CaliperCandidate>> groups, PointF arrangeDir, float tolerance, LineDetectionParams parameters)
        {
            LineHypothesis best = LineHypothesis.Invalid;
            int count = candidates.Count;
            
            // 优化：最多只检查前10个候选点的组合
            int maxPairs = Math.Min(count, 10);
            
            for (int i = 0; i < maxPairs - 1; i++)
            {
                for (int j = i + 1; j < maxPairs; j++)
                {
                    if (candidates[i].CaliperIndex == candidates[j].CaliperIndex) continue;

                    LineHypothesis hypothesis = LineHypothesis.FromPoints(candidates[i].Point, candidates[j].Point);
                    if (!hypothesis.IsValid) continue;

                    float orientation = Math.Abs(Dot(hypothesis.Direction, arrangeDir));
                    if (orientation < 0.5f) continue;

                    float score = ScoreHypothesisOptimized(hypothesis, groups, tolerance, parameters);
                    score += orientation;
                    if (score > best.Score)
                    {
                        best = hypothesis.WithScore(score);
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// 优化的评分计算，减少重复计算
        /// </summary>
        private static float ScoreHypothesisOptimized(LineHypothesis hypothesis, List<List<CaliperCandidate>> groups, float tolerance, LineDetectionParams parameters)
        {
            float score = 0f;
            int continuous = 0;
            int bestContinuous = 0;

            foreach (List<CaliperCandidate> group in groups)
            {
                if (group.Count == 0)
                {
                    continuous = 0;
                    continue;
                }

                // 优化：直接找最强的候选点，而不是遍历所有候选点
                CaliperCandidate bestCandidate = group[0];
                float bestDistance = float.MaxValue;
                
                foreach (CaliperCandidate candidate in group)
                {
                    float dx = candidate.Point.X - hypothesis.Point.X;
                    float dy = candidate.Point.Y - hypothesis.Point.Y;
                    float distance = Math.Abs(dx * hypothesis.Normal.X + dy * hypothesis.Normal.Y);
                    
                    if (distance <= tolerance && (distance < bestDistance || candidate.Strength > bestCandidate.Strength))
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                    }
                }

                if (bestDistance <= tolerance)
                {
                    score += 1.0f + (1.0f - bestDistance / tolerance) * 0.5f;
                    continuous++;
                    if (continuous > bestContinuous) bestContinuous = continuous;
                }
                else
                {
                    continuous = 0;
                }
            }

            score += bestContinuous * 0.2f;
            return score;
        }

        /// <summary>
        /// 优化的内点选择
        /// </summary>
        private static List<LineEdgePoint> SelectInliersOptimized(List<List<CaliperCandidate>> groups, LineHypothesis hypothesis, float tolerance, LineDetectionParams parameters)
        {
            List<LineEdgePoint> selected = new List<LineEdgePoint>(groups.Count);
            
            foreach (List<CaliperCandidate> group in groups)
            {
                if (group.Count == 0) continue;

                CaliperCandidate bestCandidate = group[0];
                float bestDistance = float.MaxValue;
                
                foreach (CaliperCandidate candidate in group)
                {
                    float dx = candidate.Point.X - hypothesis.Point.X;
                    float dy = candidate.Point.Y - hypothesis.Point.Y;
                    float distance = Math.Abs(dx * hypothesis.Normal.X + dy * hypothesis.Normal.Y);
                    
                    if (distance <= tolerance && (distance < bestDistance || candidate.Strength > bestCandidate.Strength))
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                    }
                }

                if (bestDistance <= tolerance)
                {
                    selected.Add(new LineEdgePoint(bestCandidate.Point, bestCandidate.Strength));
                }
            }
            
            return selected;
        }

        /// <summary>
        /// 优化的回退点选择，避免排序
        /// </summary>
        private static List<LineEdgePoint> SelectFallbackPointsOptimized(List<List<CaliperCandidate>> candidateGroups, LineDetectionParams parameters)
        {
            List<LineEdgePoint> result = new List<LineEdgePoint>(candidateGroups.Count);
            
            foreach (List<CaliperCandidate> group in candidateGroups)
            {
                if (group == null || group.Count == 0) continue;
                
                CaliperCandidate selected = SelectCandidateByMode(group, parameters.SelectionMode);
                result.Add(new LineEdgePoint(selected.Point, selected.Strength));
            }
            
            return result;
        }

        /// <summary>
        /// 按指定选择模式从单条卡尺候选点中选择一个边缘点。
        /// </summary>
        /// <param name="group">单条卡尺上的候选边缘点集合。</param>
        /// <param name="selectionMode">候选边缘选择模式。</param>
        /// <returns>符合选择模式的候选边缘点。</returns>
        private static CaliperCandidate SelectCandidateByMode(List<CaliperCandidate> group, LineSelectionMode selectionMode)
        {
            CaliperCandidate selected = group[0];

            switch (selectionMode)
            {
                case LineSelectionMode.First:
                    foreach (CaliperCandidate c in group)
                    {
                        if (c.Offset < selected.Offset) selected = c;
                    }
                    break;
                case LineSelectionMode.Last:
                    foreach (CaliperCandidate c in group)
                    {
                        if (c.Offset > selected.Offset) selected = c;
                    }
                    break;
                default:
                    foreach (CaliperCandidate c in group)
                    {
                        if (c.Strength > selected.Strength) selected = c;
                    }
                    break;
            }

            return selected;
        }

        /// <summary>
        /// 将内部候选点转换为直线检测输出点。
        /// </summary>
        /// <param name="candidates">内部候选边缘点集合。</param>
        /// <returns>直线检测输出点集合。</returns>
        private static List<LineEdgePoint> ToLineEdgePoints(List<CaliperCandidate> candidates)
        {
            List<LineEdgePoint> result = new List<LineEdgePoint>(candidates.Count);
            foreach (CaliperCandidate candidate in candidates)
            {
                result.Add(new LineEdgePoint(candidate.Point, candidate.Strength));
            }

            return result;
        }

        private static bool MatchPolarity(float gradient, LineEdgePolarity polarity)
        {
            switch (polarity)
            {
                case LineEdgePolarity.Positive:
                    return gradient > 0f;
                case LineEdgePolarity.Negative:
                    return gradient < 0f;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 按剔除距离和剔除比例过滤拟合外点。
        /// </summary>
        /// <param name="points">原始检测点集合。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <returns>过滤后的检测点集合。</returns>
        private static List<LineEdgePoint> RejectOutliers(List<LineEdgePoint> points, LineDetectionParams parameters)
        {
            if (points.Count < 3 || parameters.RejectDistance <= 0 || parameters.RejectRatio <= 0) return points;

            FittedLine line = FitByLeastSquares(points);
            if (!line.IsValid) return points;

            List<PointDistance> distances = new List<PointDistance>(points.Count);
            foreach (LineEdgePoint point in points)
            {
                distances.Add(new PointDistance(point, DistanceToLine(point.Point, line)));
            }

            int maxRemoveCount = (int)Math.Floor(points.Count * parameters.RejectRatio / 100f);
            maxRemoveCount = Math.Min(points.Count - 2, Math.Max(0, maxRemoveCount));
            if (maxRemoveCount <= 0) return points;

            List<PointDistance> rejected = distances
                .Where(x => x.Distance > parameters.RejectDistance)
                .OrderByDescending(x => x.Distance)
                .Take(maxRemoveCount)
                .ToList();
            if (rejected.Count == 0) return points;

            HashSet<LineEdgePoint> rejectedPoints = new HashSet<LineEdgePoint>(rejected.Select(x => x.Point));
            List<LineEdgePoint> filtered = points
                .Where(x => !rejectedPoints.Contains(x))
                .ToList();

            return filtered.Count >= 2 ? filtered : points;
        }

        /// <summary>
        /// 按拟合参数生成裁剪到ROI内部的结果线段。
        /// </summary>
        /// <param name="frame">直线检测测量框。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <param name="edgePoints">参与拟合的检测点集合。</param>
        /// <returns>结果线段的起点和终点。</returns>
        private static PointF[] FitLine(LineDetectionFrame frame, LineDetectionParams parameters, List<LineEdgePoint> edgePoints)
        {
            FittedLine line = FitLine(edgePoints, parameters);
            if (!line.IsValid)
            {
                line = FitByLeastSquares(edgePoints);
            }

            PointF direction = line.Direction;
            PointF point = line.Point;
            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            if (direction.X * arrangeDir.X + direction.Y * arrangeDir.Y < 0f)
            {
                direction = new PointF(-direction.X, -direction.Y);
            }

            float halfLength = frame.GetArrangeLength(parameters.ScanDirection) / 2f;
            PointF start = new PointF(point.X - direction.X * halfLength, point.Y - direction.Y * halfLength);
            PointF end = new PointF(point.X + direction.X * halfLength, point.Y + direction.Y * halfLength);
            return ClipLineToFrame(point, direction, frame, out PointF clippedStart, out PointF clippedEnd)
                ? new[] { clippedStart, clippedEnd }
                : new[] { start, end };
        }

        /// <summary>
        /// 使用指定方式拟合直线。
        /// </summary>
        /// <param name="points">参与拟合的检测点集合。</param>
        /// <param name="parameters">直线检测参数。</param>
        /// <returns>拟合出的无限直线。</returns>
        private static FittedLine FitLine(List<LineEdgePoint> points, LineDetectionParams parameters)
        {
            switch (parameters.FitMode)
            {
                case LineFitMode.LeastSquares:
                    return FitByLeastSquares(points);
                case LineFitMode.Huber:
                    return FitByHuber(points, parameters.RejectDistance);
                case LineFitMode.Ransac:
                    return FitByRansac(points, parameters.RejectDistance);
                default:
                    return FitByLocal(points, parameters.RejectDistance);
            }
        }

        /// <summary>
        /// 使用局部拟合方式拟合直线。
        /// </summary>
        /// <param name="points">参与拟合的检测点集合。</param>
        /// <param name="rejectDistance">局部点距离阈值。</param>
        /// <returns>拟合出的无限直线。</returns>
        private static FittedLine FitByLocal(List<LineEdgePoint> points, int rejectDistance)
        {
            if (points.Count <= 3) return FitByLeastSquares(points);

            FittedLine first = FitByLeastSquares(points);
            if (!first.IsValid) return first;

            float limit = Math.Max(1f, rejectDistance);
            List<LineEdgePoint> local = points
                .Select(x => new PointDistance(x, DistanceToLine(x.Point, first)))
                .Where(x => x.Distance <= limit)
                .OrderBy(x => x.Distance)
                .Select(x => x.Point)
                .ToList();

            return local.Count >= 2 ? FitByLeastSquares(local) : first;
        }

        /// <summary>
        /// 使用最小二乘拟合直线。
        /// </summary>
        /// <param name="points">参与拟合的检测点集合。</param>
        /// <returns>拟合出的无限直线。</returns>
        private static FittedLine FitByLeastSquares(List<LineEdgePoint> points)
        {
            if (points == null || points.Count < 2) return FittedLine.Invalid;

            double meanX = points.Average(x => x.Point.X);
            double meanY = points.Average(x => x.Point.Y);
            double sxx = 0d;
            double syy = 0d;
            double sxy = 0d;
            foreach (LineEdgePoint edgePoint in points)
            {
                double dx = edgePoint.Point.X - meanX;
                double dy = edgePoint.Point.Y - meanY;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            if (sxx + syy <= 0.000001d) return FittedLine.Invalid;

            double theta = 0.5d * Math.Atan2(2d * sxy, sxx - syy);
            PointF direction = Normalize(new PointF((float)Math.Cos(theta), (float)Math.Sin(theta)));
            return FittedLine.FromPointDirection(new PointF((float)meanX, (float)meanY), direction);
        }

        /// <summary>
        /// 使用Huber权重拟合直线。
        /// </summary>
        /// <param name="points">参与拟合的检测点集合。</param>
        /// <param name="rejectDistance">Huber距离阈值。</param>
        /// <returns>拟合出的无限直线。</returns>
        private static FittedLine FitByHuber(List<LineEdgePoint> points, int rejectDistance)
        {
            FittedLine line = FitByLeastSquares(points);
            if (!line.IsValid) return line;

            float delta = Math.Max(1f, rejectDistance);
            for (int iteration = 0; iteration < 6; iteration++)
            {
                double weightSum = 0d;
                double meanX = 0d;
                double meanY = 0d;
                foreach (LineEdgePoint point in points)
                {
                    float distance = DistanceToLine(point.Point, line);
                    double weight = distance <= delta ? 1d : delta / Math.Max(distance, 0.0001f);
                    weightSum += weight;
                    meanX += point.Point.X * weight;
                    meanY += point.Point.Y * weight;
                }

                if (weightSum <= 0d) break;
                meanX /= weightSum;
                meanY /= weightSum;

                double sxx = 0d;
                double syy = 0d;
                double sxy = 0d;
                foreach (LineEdgePoint point in points)
                {
                    float distance = DistanceToLine(point.Point, line);
                    double weight = distance <= delta ? 1d : delta / Math.Max(distance, 0.0001f);
                    double dx = point.Point.X - meanX;
                    double dy = point.Point.Y - meanY;
                    sxx += weight * dx * dx;
                    syy += weight * dy * dy;
                    sxy += weight * dx * dy;
                }

                double theta = 0.5d * Math.Atan2(2d * sxy, sxx - syy);
                line = FittedLine.FromPointDirection(new PointF((float)meanX, (float)meanY), Normalize(new PointF((float)Math.Cos(theta), (float)Math.Sin(theta))));
                if (!line.IsValid) break;
            }

            return line;
        }

        /// <summary>
        /// 使用简化RANSAC拟合直线。
        /// </summary>
        /// <param name="points">参与拟合的检测点集合。</param>
        /// <param name="rejectDistance">内点距离阈值。</param>
        /// <returns>拟合出的无限直线。</returns>
        private static FittedLine FitByRansac(List<LineEdgePoint> points, int rejectDistance)
        {
            if (points.Count < 2) return FittedLine.Invalid;

            float tolerance = Math.Max(1f, rejectDistance);
            FittedLine best = FittedLine.Invalid;
            int bestCount = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    FittedLine candidate = FittedLine.FromPoints(points[i].Point, points[j].Point);
                    if (!candidate.IsValid) continue;

                    int count = 0;
                    float distanceSum = 0f;
                    foreach (LineEdgePoint point in points)
                    {
                        float distance = DistanceToLine(point.Point, candidate);
                        if (distance <= tolerance)
                        {
                            count++;
                            distanceSum += distance;
                        }
                    }

                    if (count > bestCount || (count == bestCount && distanceSum < bestDistance))
                    {
                        best = candidate;
                        bestCount = count;
                        bestDistance = distanceSum;
                    }
                }
            }

            if (!best.IsValid) return FitByLeastSquares(points);

            List<LineEdgePoint> inliers = points.Where(x => DistanceToLine(x.Point, best) <= tolerance).ToList();
            return inliers.Count >= 2 ? FitByLeastSquares(inliers) : best;
        }

        /// <summary>
        /// 计算点到直线的距离。
        /// </summary>
        /// <param name="point">待计算的点。</param>
        /// <param name="line">拟合直线。</param>
        /// <returns>点到直线的垂直距离。</returns>
        private static float DistanceToLine(PointF point, FittedLine line)
        {
            PointF delta = new PointF(point.X - line.Point.X, point.Y - line.Point.Y);
            return Math.Abs(Dot(delta, line.Normal));
        }

        /// <summary>
        /// 归一化向量。
        /// </summary>
        /// <param name="point">输入向量。</param>
        /// <returns>单位向量，长度过小时返回空点。</returns>
        private static PointF Normalize(PointF point)
        {
            float length = (float)Math.Sqrt(point.X * point.X + point.Y * point.Y);
            return length <= 0.000001f ? PointF.Empty : new PointF(point.X / length, point.Y / length);
        }

        private static bool ClipLineToFrame(PointF point, PointF direction, LineDetectionFrame frame, out PointF start, out PointF end)
        {
            start = PointF.Empty;
            end = PointF.Empty;
            PointF[] corners = frame.GetCorners();
            List<PointF> intersections = new List<PointF>();
            
            for (int i = 0; i < corners.Length; i++)
            {
                PointF a = corners[i];
                PointF b = corners[(i + 1) % corners.Length];
                if (TryIntersectInfiniteLineWithSegment(point, direction, a, b, out PointF intersection))
                {
                    AddUniquePoint(intersections, intersection);
                }
            }

            if (intersections.Count < 2) return false;

            float maxDistance = float.NegativeInfinity;
            for (int i = 0; i < intersections.Count - 1; i++)
            {
                for (int j = i + 1; j < intersections.Count; j++)
                {
                    float dx = intersections[i].X - intersections[j].X;
                    float dy = intersections[i].Y - intersections[j].Y;
                    float distance = dx * dx + dy * dy;
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        start = intersections[i];
                        end = intersections[j];
                    }
                }
            }
            
            return maxDistance > 0.0001f;
        }

        private static bool TryIntersectInfiniteLineWithSegment(PointF linePoint, PointF lineDirection, PointF segStart, PointF segEnd, out PointF intersection)
        {
            intersection = PointF.Empty;
            PointF segmentDirection = new PointF(segEnd.X - segStart.X, segEnd.Y - segStart.Y);
            float denominator = lineDirection.X * segmentDirection.Y - lineDirection.Y * segmentDirection.X;
            if (Math.Abs(denominator) < 0.0001f) return false;

            PointF delta = new PointF(segStart.X - linePoint.X, segStart.Y - linePoint.Y);
            float u = (delta.X * lineDirection.Y - delta.Y * lineDirection.X) / denominator;
            if (u < -0.0001f || u > 1.0001f) return false;

            intersection = new PointF(segStart.X + segmentDirection.X * u, segStart.Y + segmentDirection.Y * u);
            return true;
        }

        private static void AddUniquePoint(List<PointF> points, PointF point)
        {
            foreach (PointF existing in points)
            {
                float dx = existing.X - point.X;
                float dy = existing.Y - point.Y;
                if (dx * dx + dy * dy < 0.01f) return;
            }
            points.Add(point);
        }

        private static float Dot(PointF a, PointF b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        private struct ProfileSample
        {
            public readonly PointF Point;
            public readonly float Offset;
            public readonly float GrayValue;
            public readonly float SobelValue;

            public ProfileSample(PointF point, float offset, float grayValue, float sobelValue)
            {
                Point = point;
                Offset = offset;
                GrayValue = grayValue;
                SobelValue = sobelValue;
            }
        }

        private struct CaliperCandidate
        {
            public static readonly CaliperCandidate Invalid = new CaliperCandidate(PointF.Empty, 0f, 0f, -1);

            public readonly PointF Point;
            public readonly float Offset;
            public readonly float Strength;
            public readonly int CaliperIndex;

            public CaliperCandidate(PointF point, float offset, float strength, int caliperIndex)
            {
                Point = point;
                Offset = offset;
                Strength = strength;
                CaliperIndex = caliperIndex;
            }

            public bool IsValid => CaliperIndex >= 0;
        }

        private struct LineHypothesis
        {
            public static readonly LineHypothesis Invalid = new LineHypothesis(PointF.Empty, PointF.Empty, PointF.Empty, float.NegativeInfinity, false);

            public readonly PointF Point;
            public readonly PointF Direction;
            public readonly PointF Normal;
            public readonly float Score;
            public readonly bool IsValid;

            private LineHypothesis(PointF point, PointF direction, PointF normal, float score, bool isValid)
            {
                Point = point;
                Direction = direction;
                Normal = normal;
                Score = score;
                IsValid = isValid;
            }

            public static LineHypothesis FromPoints(PointF a, PointF b)
            {
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0.0001f) return Invalid;

                PointF direction = new PointF(dx / length, dy / length);
                PointF normal = new PointF(-direction.Y, direction.X);
                return new LineHypothesis(a, direction, normal, 0f, true);
            }

            public LineHypothesis WithScore(float score)
            {
                return new LineHypothesis(Point, Direction, Normal, score, IsValid);
            }
        }

        private struct FittedLine
        {
            public static readonly FittedLine Invalid = new FittedLine(PointF.Empty, PointF.Empty, PointF.Empty, false);

            public readonly PointF Point;
            public readonly PointF Direction;
            public readonly PointF Normal;
            public readonly bool IsValid;

            private FittedLine(PointF point, PointF direction, PointF normal, bool isValid)
            {
                Point = point;
                Direction = direction;
                Normal = normal;
                IsValid = isValid;
            }

            /// <summary>
            /// 按直线经过点和方向创建拟合直线。
            /// </summary>
            /// <param name="point">直线经过的点。</param>
            /// <param name="direction">直线方向。</param>
            /// <returns>拟合直线。</returns>
            public static FittedLine FromPointDirection(PointF point, PointF direction)
            {
                float length = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                if (length <= 0.000001f) return Invalid;

                PointF unit = new PointF(direction.X / length, direction.Y / length);
                PointF normal = new PointF(-unit.Y, unit.X);
                return new FittedLine(point, unit, normal, true);
            }

            /// <summary>
            /// 按两个点创建拟合直线。
            /// </summary>
            /// <param name="start">起点。</param>
            /// <param name="end">终点。</param>
            /// <returns>拟合直线。</returns>
            public static FittedLine FromPoints(PointF start, PointF end)
            {
                return FromPointDirection(start, new PointF(end.X - start.X, end.Y - start.Y));
            }
        }

        private struct PointDistance
        {
            public readonly LineEdgePoint Point;
            public readonly float Distance;

            /// <summary>
            /// 初始化检测点到拟合直线的距离记录。
            /// </summary>
            /// <param name="point">检测点。</param>
            /// <param name="distance">点到直线的距离。</param>
            public PointDistance(LineEdgePoint point, float distance)
            {
                Point = point;
                Distance = distance;
            }
        }
    }
}
