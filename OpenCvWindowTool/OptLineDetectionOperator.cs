using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace OpenCvWindowTool
{
    /// <summary>
    /// 按SciFindLine风格执行ROI直线检测。
    /// </summary>
    public sealed class OptLineDetectionOperator
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
            DetectionPoints points = CollectEdgePoints(context, frame, actualParams);
            if (points.Selected.Count < 2)
            {
                return LineDetectionResult.CreateFailure("有效检测点不足，无法拟合直线。", frame, actualParams.ScanDirection, points.Selected, stopwatch.Elapsed);
            }

            List<LineEdgePoint> fittingPoints = RejectOutliers(points.Selected, actualParams);
            if (fittingPoints.Count < 2)
            {
                fittingPoints = points.Selected;
            }

            FittedLine line = FitLine(fittingPoints, actualParams);
            if (!line.IsValid)
            {
                return LineDetectionResult.CreateFailure("直线拟合失败。", frame, actualParams.ScanDirection, points.Selected, stopwatch.Elapsed);
            }

            PointF[] segment = BuildLineSegment(line, frame, fittingPoints);
            PointF middle = new PointF((segment[0].X + segment[1].X) * 0.5f, (segment[0].Y + segment[1].Y) * 0.5f);
            return LineDetectionResult.CreateSuccess(frame, actualParams.ScanDirection, segment[0], middle, segment[1], points.Selected, points.First, points.Last, stopwatch.Elapsed);
        }

        /// <summary>
        /// 归一化检测参数，避免非法输入进入检测流程。
        /// </summary>
        /// <param name="parameters">原始参数。</param>
        /// <returns>归一化后的参数。</returns>
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
                ProfileLineIndex = Math.Max(1, source.ProfileLineIndex),
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
        /// 把数值调整为奇数。
        /// </summary>
        /// <param name="value">输入数值。</param>
        /// <returns>奇数数值。</returns>
        private static int MakeOdd(int value)
        {
            return value % 2 == 0 ? value + 1 : value;
        }

        /// <summary>
        /// 收集ROI内所有搜索线上的边缘点。
        /// </summary>
        /// <param name="context">灰度图上下文。</param>
        /// <param name="frame">检测测量框。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>检测点集合。</returns>
        private static DetectionPoints CollectEdgePoints(LineDetectionImageContext context, LineDetectionFrame frame, LineDetectionParams parameters)
        {
            DetectionPoints result = new DetectionPoints(parameters.SampleCount);
            PointF scanDir = frame.GetScanDirection(parameters.ScanDirection);
            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            float arrangeLength = frame.GetArrangeLength(parameters.ScanDirection);
            float scanLength = frame.GetScanLength(parameters.ScanDirection);
            float arrangeStart = -arrangeLength / 2f;
            float arrangeStep = arrangeLength / parameters.SampleCount;

            for (int i = 0; i < parameters.SampleCount; i++)
            {
                float along = arrangeStart + arrangeStep * (i + 0.5f);
                PointF center = new PointF(frame.Center.X + arrangeDir.X * along, frame.Center.Y + arrangeDir.Y * along);
                List<CaliperCandidate> candidates = DetectOneSearchLine(context, center, scanDir, arrangeDir, scanLength, i, parameters);
                if (candidates.Count == 0) continue;

                CaliperCandidate first = candidates[0];
                CaliperCandidate last = candidates[candidates.Count - 1];
                CaliperCandidate best = candidates[0];
                foreach (CaliperCandidate candidate in candidates)
                {
                    if (candidate.Strength > best.Strength) best = candidate;
                }

                result.First.Add(first.ToLineEdgePoint());
                result.Last.Add(last.ToLineEdgePoint());
                result.Selected.Add(SelectCandidate(first, last, best, parameters.SelectionMode).ToLineEdgePoint());
            }

            return result;
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
        /// 检查梯度方向是否符合边缘极性。
        /// </summary>
        /// <param name="gradient">梯度值。</param>
        /// <param name="polarity">边缘极性。</param>
        /// <returns>符合时返回true。</returns>
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
        /// 按取点策略选择候选边缘。
        /// </summary>
        /// <param name="first">第一条边缘。</param>
        /// <param name="last">最后一条边缘。</param>
        /// <param name="best">最佳边缘。</param>
        /// <param name="selectionMode">取点策略。</param>
        /// <returns>选中的候选边缘。</returns>
        private static CaliperCandidate SelectCandidate(CaliperCandidate first, CaliperCandidate last, CaliperCandidate best, LineSelectionMode selectionMode)
        {
            switch (selectionMode)
            {
                case LineSelectionMode.First:
                    return first;
                case LineSelectionMode.Last:
                    return last;
                default:
                    return best;
            }
        }

        /// <summary>
        /// 按剔除距离和剔除比例过滤拟合线外点。
        /// </summary>
        /// <param name="points">原始点集合。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>剔除后的点集合。</returns>
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
        /// 使用指定方式拟合直线。
        /// </summary>
        /// <param name="points">拟合点集合。</param>
        /// <param name="parameters">检测参数。</param>
        /// <returns>拟合直线。</returns>
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
        /// <param name="points">拟合点集合。</param>
        /// <param name="rejectDistance">剔除距离。</param>
        /// <returns>拟合直线。</returns>
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
        /// <param name="points">拟合点集合。</param>
        /// <returns>拟合直线。</returns>
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
        /// <param name="points">拟合点集合。</param>
        /// <param name="rejectDistance">Huber距离阈值。</param>
        /// <returns>拟合直线。</returns>
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
        /// <param name="points">拟合点集合。</param>
        /// <param name="rejectDistance">内点距离阈值。</param>
        /// <returns>拟合直线。</returns>
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
        /// 按检测点范围生成有限线段。
        /// </summary>
        /// <param name="line">拟合直线。</param>
        /// <param name="points">检测点集合。</param>
        /// <returns>线段起点和终点。</returns>
        private static PointF[] BuildLineSegment(FittedLine line, LineDetectionFrame frame, List<LineEdgePoint> fallbackPoints)
        {
            return TryClipLineToFrame(line, frame, out PointF start, out PointF end)
                ? new[] { start, end }
                : BuildLineSegmentFromPoints(line, fallbackPoints);
        }

        /// <summary>
        /// 按检测点范围生成有限线段。
        /// </summary>
        /// <param name="line">拟合直线。</param>
        /// <param name="points">检测点集合。</param>
        /// <returns>线段起点和终点。</returns>
        private static PointF[] BuildLineSegmentFromPoints(FittedLine line, List<LineEdgePoint> points)
        {
            float minProjection = float.MaxValue;
            float maxProjection = float.MinValue;
            foreach (LineEdgePoint point in points)
            {
                float projection = Dot(Subtract(point.Point, line.Point), line.Direction);
                minProjection = Math.Min(minProjection, projection);
                maxProjection = Math.Max(maxProjection, projection);
            }

            if (maxProjection - minProjection < 0.001f)
            {
                minProjection -= 1f;
                maxProjection += 1f;
            }

            return new[]
            {
                Add(line.Point, Scale(line.Direction, minProjection)),
                Add(line.Point, Scale(line.Direction, maxProjection))
            };
        }

        /// <summary>
        /// 把无限拟合直线裁剪到检测测量框内部。
        /// </summary>
        /// <param name="line">拟合直线。</param>
        /// <param name="frame">检测测量框。</param>
        /// <param name="start">裁剪后的起点。</param>
        /// <param name="end">裁剪后的终点。</param>
        /// <returns>成功得到有效线段时返回true。</returns>
        private static bool TryClipLineToFrame(FittedLine line, LineDetectionFrame frame, out PointF start, out PointF end)
        {
            start = PointF.Empty;
            end = PointF.Empty;
            if (!line.IsValid || !frame.IsValid) return false;

            PointF[] corners = frame.GetCorners();
            List<PointF> intersections = new List<PointF>();
            for (int i = 0; i < corners.Length; i++)
            {
                PointF a = corners[i];
                PointF b = corners[(i + 1) % corners.Length];
                if (TryIntersectLineSegment(line.Point, line.Direction, a, b, out PointF intersection))
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

        /// <summary>
        /// 计算无限直线和有限线段的交点。
        /// </summary>
        /// <param name="linePoint">直线经过的点。</param>
        /// <param name="lineDirection">直线方向。</param>
        /// <param name="segmentStart">线段起点。</param>
        /// <param name="segmentEnd">线段终点。</param>
        /// <param name="intersection">交点。</param>
        /// <returns>存在交点且交点在线段范围内时返回true。</returns>
        private static bool TryIntersectLineSegment(PointF linePoint, PointF lineDirection, PointF segmentStart, PointF segmentEnd, out PointF intersection)
        {
            intersection = PointF.Empty;
            PointF segmentDirection = Subtract(segmentEnd, segmentStart);
            float denominator = lineDirection.X * segmentDirection.Y - lineDirection.Y * segmentDirection.X;
            if (Math.Abs(denominator) < 0.0001f) return false;

            PointF delta = Subtract(segmentStart, linePoint);
            float u = (delta.X * lineDirection.Y - delta.Y * lineDirection.X) / denominator;
            if (u < -0.0001f || u > 1.0001f) return false;

            intersection = Add(segmentStart, Scale(segmentDirection, u));
            return true;
        }

        /// <summary>
        /// 向点集合加入非重复点。
        /// </summary>
        /// <param name="points">点集合。</param>
        /// <param name="point">待加入的点。</param>
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

        /// <summary>
        /// 计算点到直线距离。
        /// </summary>
        /// <param name="point">点。</param>
        /// <param name="line">直线。</param>
        /// <returns>距离。</returns>
        private static float DistanceToLine(PointF point, FittedLine line)
        {
            PointF delta = Subtract(point, line.Point);
            return Math.Abs(Dot(delta, line.Normal));
        }

        /// <summary>
        /// 向量点积。
        /// </summary>
        /// <param name="a">第一个向量。</param>
        /// <param name="b">第二个向量。</param>
        /// <returns>点积结果。</returns>
        private static float Dot(PointF a, PointF b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        /// <summary>
        /// 向量相减。
        /// </summary>
        /// <param name="a">被减向量。</param>
        /// <param name="b">减去的向量。</param>
        /// <returns>相减后的向量。</returns>
        private static PointF Subtract(PointF a, PointF b)
        {
            return new PointF(a.X - b.X, a.Y - b.Y);
        }

        /// <summary>
        /// 向量相加。
        /// </summary>
        /// <param name="a">第一个向量。</param>
        /// <param name="b">第二个向量。</param>
        /// <returns>相加后的向量。</returns>
        private static PointF Add(PointF a, PointF b)
        {
            return new PointF(a.X + b.X, a.Y + b.Y);
        }

        /// <summary>
        /// 向量缩放。
        /// </summary>
        /// <param name="point">输入向量。</param>
        /// <param name="scale">缩放比例。</param>
        /// <returns>缩放后的向量。</returns>
        private static PointF Scale(PointF point, float scale)
        {
            return new PointF(point.X * scale, point.Y * scale);
        }

        /// <summary>
        /// 归一化向量。
        /// </summary>
        /// <param name="point">输入向量。</param>
        /// <returns>单位向量；长度过小时返回空点。</returns>
        private static PointF Normalize(PointF point)
        {
            float length = (float)Math.Sqrt(point.X * point.X + point.Y * point.Y);
            return length <= 0.000001f ? PointF.Empty : new PointF(point.X / length, point.Y / length);
        }

        private sealed class DetectionPoints
        {
            /// <summary>
            /// 初始化检测点集合。
            /// </summary>
            /// <param name="capacity">集合初始容量。</param>
            public DetectionPoints(int capacity)
            {
                Selected = new List<LineEdgePoint>(capacity);
                First = new List<LineEdgePoint>(capacity);
                Last = new List<LineEdgePoint>(capacity);
            }

            /// <summary>
            /// 获取参与拟合的检测点。
            /// </summary>
            public List<LineEdgePoint> Selected { get; private set; }

            /// <summary>
            /// 获取每条搜索线上的第一条边缘点。
            /// </summary>
            public List<LineEdgePoint> First { get; private set; }

            /// <summary>
            /// 获取每条搜索线上的最后一条边缘点。
            /// </summary>
            public List<LineEdgePoint> Last { get; private set; }
        }

        private struct CaliperCandidate
        {
            /// <summary>
            /// 无效候选边缘点。
            /// </summary>
            public static readonly CaliperCandidate Invalid = new CaliperCandidate(PointF.Empty, 0f, 0f, -1);

            /// <summary>
            /// 候选边缘点坐标。
            /// </summary>
            public readonly PointF Point;

            /// <summary>
            /// 候选点在搜索方向上的偏移。
            /// </summary>
            public readonly float Offset;

            /// <summary>
            /// 候选点边缘强度。
            /// </summary>
            public readonly float Strength;

            /// <summary>
            /// 候选点所属搜索线编号。
            /// </summary>
            public readonly int LineIndex;

            /// <summary>
            /// 候选点是否有效。
            /// </summary>
            public bool IsValid => LineIndex >= 0;

            /// <summary>
            /// 初始化候选边缘点。
            /// </summary>
            /// <param name="point">候选边缘点坐标。</param>
            /// <param name="offset">搜索方向偏移。</param>
            /// <param name="strength">边缘强度。</param>
            /// <param name="lineIndex">搜索线编号。</param>
            public CaliperCandidate(PointF point, float offset, float strength, int lineIndex)
            {
                Point = point;
                Offset = offset;
                Strength = strength;
                LineIndex = lineIndex;
            }

            /// <summary>
            /// 转换为公开的检测点对象。
            /// </summary>
            /// <returns>检测点对象。</returns>
            public LineEdgePoint ToLineEdgePoint()
            {
                return new LineEdgePoint(Point, Strength);
            }
        }

        private struct PointDistance
        {
            /// <summary>
            /// 检测点。
            /// </summary>
            public readonly LineEdgePoint Point;

            /// <summary>
            /// 点到拟合线距离。
            /// </summary>
            public readonly float Distance;

            /// <summary>
            /// 初始化点距离数据。
            /// </summary>
            /// <param name="point">检测点。</param>
            /// <param name="distance">点到拟合线距离。</param>
            public PointDistance(LineEdgePoint point, float distance)
            {
                Point = point;
                Distance = distance;
            }
        }

        private struct FittedLine
        {
            /// <summary>
            /// 无效拟合线。
            /// </summary>
            public static readonly FittedLine Invalid = new FittedLine(PointF.Empty, PointF.Empty, PointF.Empty, false);

            /// <summary>
            /// 直线经过的点。
            /// </summary>
            public readonly PointF Point;

            /// <summary>
            /// 直线方向。
            /// </summary>
            public readonly PointF Direction;

            /// <summary>
            /// 直线法向。
            /// </summary>
            public readonly PointF Normal;

            /// <summary>
            /// 拟合线是否有效。
            /// </summary>
            public readonly bool IsValid;

            /// <summary>
            /// 初始化拟合线。
            /// </summary>
            /// <param name="point">直线经过的点。</param>
            /// <param name="direction">直线方向。</param>
            /// <param name="normal">直线法向。</param>
            /// <param name="isValid">是否有效。</param>
            private FittedLine(PointF point, PointF direction, PointF normal, bool isValid)
            {
                Point = point;
                Direction = direction;
                Normal = normal;
                IsValid = isValid;
            }

            /// <summary>
            /// 从两点创建拟合直线。
            /// </summary>
            /// <param name="a">第一个点。</param>
            /// <param name="b">第二个点。</param>
            /// <returns>拟合直线。</returns>
            public static FittedLine FromPoints(PointF a, PointF b)
            {
                return FromPointDirection(a, Normalize(new PointF(b.X - a.X, b.Y - a.Y)));
            }

            /// <summary>
            /// 从点和方向创建拟合直线。
            /// </summary>
            /// <param name="point">直线经过的点。</param>
            /// <param name="direction">直线方向。</param>
            /// <returns>拟合直线。</returns>
            public static FittedLine FromPointDirection(PointF point, PointF direction)
            {
                if (direction == PointF.Empty) return Invalid;
                PointF normal = new PointF(-direction.Y, direction.X);
                return new FittedLine(point, direction, normal, true);
            }
        }
    }
}
