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
            if (context == null || context.GrayImage == null || context.GrayImage.Empty())
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
            List<LineEdgePoint> edgePoints = CollectEdgePoints(context.GrayImage, frame, actualParams);
            if (edgePoints.Count < 2)
            {
                return LineDetectionResult.CreateFailure("有效检测点不足，无法拟合直线。", frame, actualParams.ScanDirection, edgePoints, stopwatch.Elapsed);
            }

            PointF[] line = FitLine(frame, actualParams, edgePoints);
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
                SmoothSize = Math.Max(1, source.SmoothSize),
                EdgePolarity = source.EdgePolarity,
                StrengthType = source.StrengthType,
                SelectionMode = source.SelectionMode,
                ScanDirection = source.ScanDirection,
                FitMode = source.FitMode
            };
            if (result.SmoothSize % 2 == 0) result.SmoothSize++;
            return result;
        }

        private static List<LineEdgePoint> CollectEdgePoints(Mat gray, LineDetectionFrame frame, LineDetectionParams parameters)
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
            float caliperHalfWidth = Math.Max(0.5f, arrangeLength / Math.Max(1, parameters.SampleCount) * 0.5f);
            //第一个卡尺相对中心点的起始偏移量
            float arrangeStart = -arrangeLength / 2f + caliperHalfWidth;
            float arrangeEnd = arrangeLength / 2f - caliperHalfWidth;
            //每个卡尺之间的固定步长
            float arrangeStep = parameters.SampleCount == 1 ? 0f : (arrangeEnd - arrangeStart) / (parameters.SampleCount - 1);

            // 优化：预计算ROI区域的Sobel梯度，只计算一次
            Mat gradMat = null;
            Rect roiRect = GetRoiBoundingRect(gray, frame, parameters);
            
            if (parameters.StrengthType == LineEdgeStrengthType.Sobel)
            {
                gradMat = new Mat();
                using (Mat roiMat = new Mat(gray, roiRect))
                using (Mat sobelX = new Mat())
                using (Mat sobelY = new Mat())
                {
                    Cv2.Sobel(roiMat, sobelX, MatType.CV_32F, 1, 0, 3);
                    Cv2.Sobel(roiMat, sobelY, MatType.CV_32F, 0, 1, 3);
                    Cv2.AddWeighted(sobelX, scanDir.X, sobelY, scanDir.Y, 0, gradMat);
                }
            }

            try
            {
                List<List<CaliperCandidate>> candidateGroups = new List<List<CaliperCandidate>>(parameters.SampleCount);
                
                for (int i = 0; i < parameters.SampleCount; i++)
                {
                    float along = arrangeStart + arrangeStep * i;
                    PointF center = new PointF(frame.Center.X + arrangeDir.X * along, frame.Center.Y + arrangeDir.Y * along);
                    List<CaliperCandidate> candidates = DetectOneCaliperOptimized(gray, gradMat, center, scanDir, arrangeDir, scanLength, caliperHalfWidth, i, parameters, roiRect);
                    
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
            finally
            {
                gradMat?.Dispose();
            }
        }

        private static Rect GetRoiBoundingRect(Mat gray, LineDetectionFrame frame, LineDetectionParams parameters)
        {
            PointF scanDir = frame.GetScanDirection(parameters.ScanDirection);
            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            float arrangeLength = frame.GetArrangeLength(parameters.ScanDirection);
            float scanLength = frame.GetScanLength(parameters.ScanDirection);
            
            PointF center = frame.Center;
            float halfArrange = arrangeLength / 2f;
            float halfScan = scanLength / 2f;
            
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    PointF corner = new PointF(
                        center.X + arrangeDir.X * halfArrange * i + scanDir.X * halfScan * j,
                        center.Y + arrangeDir.Y * halfArrange * i + scanDir.Y * halfScan * j
                    );
                    minX = Math.Min(minX, corner.X);
                    minY = Math.Min(minY, corner.Y);
                    maxX = Math.Max(maxX, corner.X);
                    maxY = Math.Max(maxY, corner.Y);
                }
            }
            
            int padding = 10;
            int x = Math.Max(0, (int)Math.Floor(minX) - padding);
            int y = Math.Max(0, (int)Math.Floor(minY) - padding);
            int width = Math.Min(gray.Width - x, (int)Math.Ceiling(maxX) - x + padding * 2);
            int height = Math.Min(gray.Height - y, (int)Math.Ceiling(maxY) - y + padding * 2);
            
            return new Rect(x, y, Math.Max(1, width), Math.Max(1, height));
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
            List<CaliperCandidate> orderedCandidates = candidateGroups
                .Where(g => g.Count > 0)
                .Select(g => SelectCandidateByMode(g, parameters.SelectionMode))
                .ToList();

            if (orderedCandidates.Count < 2)
            {
                return ToLineEdgePoints(orderedCandidates);
            }

            List<List<CaliperCandidate>> orderedGroups = orderedCandidates
                .Select(c => new List<CaliperCandidate> { c })
                .ToList();
            PointF arrangeDir = frame.GetLineArrangeDirection(parameters.ScanDirection);
            float tolerance = Math.Max(2.0f, Math.Min(8.0f, frame.GetScanLength(parameters.ScanDirection) * 0.04f));
            LineHypothesis best = FindBestLineHypothesisByAllPairs(orderedCandidates, orderedGroups, arrangeDir, tolerance, parameters);
            if (!best.IsValid)
            {
                return ToLineEdgePoints(orderedCandidates);
            }

            List<LineEdgePoint> selected = SelectInliersOptimized(orderedGroups, best, tolerance * 1.75f, parameters);
            return selected.Count >= 2 ? selected : ToLineEdgePoints(orderedCandidates);
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

        private static PointF[] FitLine(LineDetectionFrame frame, LineDetectionParams parameters, List<LineEdgePoint> edgePoints)
        {
            Point2f[] points = edgePoints.Select(x => new Point2f(x.Point.X, x.Point.Y)).ToArray();
            Line2D line = parameters.FitMode == LineFitMode.LeastSquares
                ? Cv2.FitLine(points, DistanceTypes.L2, 0, 0.01, 0.01)
                : Cv2.FitLine(points, DistanceTypes.Welsch, 0, 0.01, 0.01);

            PointF direction = new PointF((float)line.Vx, (float)line.Vy);
            PointF point = new PointF((float)line.X1, (float)line.Y1);
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
    }
}
