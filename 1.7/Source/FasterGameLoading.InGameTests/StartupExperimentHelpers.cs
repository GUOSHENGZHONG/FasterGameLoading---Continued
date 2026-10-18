using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// XML 批次追蹤純 Helper：
    /// 使用 AsyncLocal 保證 Parallel.For 之工作執行緒能繼承主緒的批次 context；
    /// 支援巢狀批次保存與還原，並以原子操作維護計數與大小分布。
    /// </summary>
    internal sealed class XmlLoadingBatchTracker
    {
        public static readonly AsyncLocal<XmlLoadingBatchTracker> CurrentContext = new();

        public string PackageId { get; set; }
        public string FolderPath { get; set; }
        public long WallStartTimestamp { get; set; }
        public long WallEndTimestamp { get; set; }

        public long ScanTicks;
        public int ExpectedFiles;

        public int AttemptedFiles;
        public int SucceededFiles;
        public int FailedFiles;
        public long CumulativeWorkerParseTicks;
        public long TotalBytes;
        public long MinBytes = long.MaxValue;
        public long MaxBytes;

        private readonly Lock sizeLock = new();

        public static XmlLoadingBatchTracker Enter(string packageId, string folderPath, out XmlLoadingBatchTracker previousState)
        {
            previousState = CurrentContext.Value;
            var newBatch = new XmlLoadingBatchTracker
            {
                PackageId = packageId,
                FolderPath = folderPath,
                WallStartTimestamp = Stopwatch.GetTimestamp(),
            };
            CurrentContext.Value = newBatch;
            return newBatch;
        }

        public static void Exit(XmlLoadingBatchTracker batch, XmlLoadingBatchTracker previousState)
        {
            if (batch != null && batch.WallEndTimestamp == 0)
            {
                batch.WallEndTimestamp = Stopwatch.GetTimestamp();
            }
            CurrentContext.Value = previousState;
        }

        public void RecordScan(long scanTicks, int expectedFiles)
        {
            Interlocked.Add(ref ScanTicks, scanTicks);
            Interlocked.Add(ref ExpectedFiles, expectedFiles);
        }

        public void RecordParseAttempt(long parseTicks, long bytes, bool success)
        {
            Interlocked.Increment(ref AttemptedFiles);
            if (success)
            {
                Interlocked.Increment(ref SucceededFiles);
            }
            else
            {
                Interlocked.Increment(ref FailedFiles);
            }

            Interlocked.Add(ref CumulativeWorkerParseTicks, parseTicks);

            if (bytes >= 0)
            {
                Interlocked.Add(ref TotalBytes, bytes);
                lock (sizeLock)
                {
                    if (bytes < MinBytes) MinBytes = bytes;
                    if (bytes > MaxBytes) MaxBytes = bytes;
                }
            }
        }
    }

    /// <summary>
    /// 區間聯集與交集純 Helper：
    /// 將多個真實 XML 批次區間合併為不重疊的聯集，再計算其與音效區間的精確交集長度。
    /// </summary>
    internal static class IntervalIntersectionHelper
    {
        public static List<(long Start, long End)> MergeIntervals(IEnumerable<(long Start, long End)> intervals)
        {
            if (intervals == null) return new List<(long Start, long End)>();

            var valid = intervals
                .Where(static i => i.End > i.Start)
                .OrderBy(static i => i.Start)
                .ThenBy(static i => i.End)
                .ToList();

            if (valid.Count is 0) return valid;

            var merged = new List<(long Start, long End)>(valid.Count);
            var current = valid[0];

            for (int i = 1; i < valid.Count; i++)
            {
                var next = valid[i];
                if (next.Start <= current.End)
                {
                    current = (current.Start, Math.Max(current.End, next.End));
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);
            return merged;
        }

        public static long CalculateOverlap(
            IEnumerable<(long Start, long End)> intervals,
            long rangeStart,
            long rangeEnd,
            bool rangeIncomplete,
            out bool isOverlapIncomplete)
        {
            isOverlapIncomplete = rangeIncomplete;

            if (rangeStart < 0 || rangeEnd <= rangeStart)
            {
                return 0;
            }

            var merged = MergeIntervals(intervals);
            long totalOverlap = 0;

            foreach (var interval in merged)
            {
                long overlapStart = Math.Max(interval.Start, rangeStart);
                long overlapEnd = Math.Min(interval.End, rangeEnd);
                if (overlapEnd > overlapStart)
                {
                    totalOverlap += overlapEnd - overlapStart;
                }
            }

            return totalOverlap;
        }
    }

    /// <summary>
    /// 測試狀態評估純 Helper：
    /// 驗證測試案例是否全部達到確定終態，不將 0 案例、未完成或中斷誤判為通過。
    /// </summary>
    internal static class TestStatusEvaluator
    {
        public enum SimplifiedStatus
        {
            Passed,
            Failed,
            Skipped,
            Canceled,
            Pending,
            NotRun,
        }

        public sealed class EvaluationResult
        {
            public int Total;
            public int Passed;
            public int Failed;
            public int Skipped;
            public int Canceled;
            public int Pending;
            public int NotRun;
            public bool AnyFixtureFailed;

            public bool IsSuccess =>
                Total > 0
                && Failed is 0
                && Canceled is 0
                && Pending is 0
                && NotRun is 0
                && !AnyFixtureFailed;
        }

        public static EvaluationResult Evaluate(IEnumerable<SimplifiedStatus> functionStatuses, bool anyFixtureFailed)
        {
            var res = new EvaluationResult
            {
                AnyFixtureFailed = anyFixtureFailed,
            };

            if (functionStatuses != null)
            {
                foreach (var status in functionStatuses)
                {
                    res.Total++;
                    switch (status)
                    {
                        case SimplifiedStatus.Passed:
                            res.Passed++;
                            break;
                        case SimplifiedStatus.Failed:
                            res.Failed++;
                            break;
                        case SimplifiedStatus.Skipped:
                            res.Skipped++;
                            break;
                        case SimplifiedStatus.Canceled:
                            res.Canceled++;
                            break;
                        case SimplifiedStatus.Pending:
                            res.Pending++;
                            break;
                        case SimplifiedStatus.NotRun:
                            res.NotRun++;
                            break;
                    }
                }
            }

            return res;
        }
    }
}
