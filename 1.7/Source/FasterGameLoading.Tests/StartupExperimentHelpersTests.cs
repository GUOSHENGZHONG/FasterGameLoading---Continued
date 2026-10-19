using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FasterGameLoading.InGameTests;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
    [TestFixture]
    public class StartupExperimentHelpersTests
    {
        [Test]
        public void XmlBatchTracker_FlowsAcrossParallelWorkers_AndAggregatesMetrics()
        {
            var tracker = XmlLoadingBatchTracker.Enter("Test.Mod", "Defs/", out var prev);
            try
            {
                tracker.RecordScan(scanTicks: 500, expectedFiles: 100);

                Parallel.For(0, 100, i =>
                {
                    var current = XmlLoadingBatchTracker.CurrentContext.Value;
                    Assert.That(current, Is.Not.Null, "CurrentContext must flow into Parallel.For worker");

                    bool success = i is not 42; // 模擬檔案 42 例外失敗
                    long fileBytes = 1000 + i * 10;
                    current.RecordParseAttempt(parseTicks: 10, bytes: fileBytes, success: success);
                });

                Assert.That(tracker.ExpectedFiles, Is.EqualTo(100));
                Assert.That(tracker.AttemptedFiles, Is.EqualTo(100));
                Assert.That(tracker.SucceededFiles, Is.EqualTo(99));
                Assert.That(tracker.FailedFiles, Is.EqualTo(1));
                Assert.That(tracker.CumulativeWorkerParseTicks, Is.EqualTo(1000));
                Assert.That(tracker.MinBytes, Is.EqualTo(1000));
                Assert.That(tracker.MaxBytes, Is.EqualTo(1990));
            }
            finally
            {
                XmlLoadingBatchTracker.Exit(tracker, prev);
            }
        }

        [Test]
        public void XmlBatchTracker_SupportsNestedBatchesAndRestoresContext()
        {
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.Null);

            var outer = XmlLoadingBatchTracker.Enter("Mod.Outer", "Defs/", out var prevOuter);
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.SameAs(outer));

            var inner = XmlLoadingBatchTracker.Enter("Mod.Inner", "Patches/", out var prevInner);
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.SameAs(inner));
            Assert.That(prevInner, Is.SameAs(outer));

            XmlLoadingBatchTracker.Exit(inner, prevInner);
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.SameAs(outer));

            XmlLoadingBatchTracker.Exit(outer, prevOuter);
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.Null);
        }

        [Test]
        public void IntervalIntersection_HandlesDisjointAndGapsCorrectly()
        {
            // 兩批 XML：[10, 20] 與 [30, 40]；音效預熱：[0, 50]
            // 中間空檔 [20, 30] 不應被當成重疊；預期重疊 = 10 + 10 = 20
            var intervals = new List<(long Start, long End)>
            {
                (10, 20),
                (30, 40)
            };

            long overlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 0,
                rangeEnd: 50,
                rangeIncomplete: false,
                out bool incomplete);

            Assert.That(incomplete, Is.False);
            Assert.That(overlap, Is.EqualTo(20));
        }

        [Test]
        public void IntervalIntersection_MergesOverlappingAndNestedBatchesWithoutDoubleCounting()
        {
            // 批次 [10, 30] 與 [20, 40] 合併為 [10, 40]
            // 音效預熱 [0, 50]，預期重疊 = 30（非 20+20=40）
            var intervals = new List<(long Start, long End)>
            {
                (10, 30),
                (20, 40)
            };

            long overlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 0,
                rangeEnd: 50,
                rangeIncomplete: false,
                out bool incomplete);

            Assert.That(incomplete, Is.False);
            Assert.That(overlap, Is.EqualTo(30));
        }

        [Test]
        public void IntervalIntersection_HandlesDisjointAndBoundaryTouch()
        {
            var intervals = new List<(long Start, long End)>
            {
                (10, 20),
                (20, 30)
            };

            // 完全不相交的情境：音效在 [50, 60]
            long disjointOverlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 50,
                rangeEnd: 60,
                rangeIncomplete: false,
                out bool disjointIncomplete);
            Assert.That(disjointIncomplete, Is.False);
            Assert.That(disjointOverlap, Is.EqualTo(0));

            // 邊界剛好相切：[20, 30] 合併為 [10, 30]，音效在 [0, 10]
            long touchOverlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 0,
                rangeEnd: 10,
                rangeIncomplete: false,
                out bool touchIncomplete);
            Assert.That(touchIncomplete, Is.False);
            Assert.That(touchOverlap, Is.EqualTo(0));
        }

        [Test]
        public void IntervalIntersection_FlagsIncompleteRangeProperly()
        {
            var intervals = new List<(long Start, long End)>
            {
                (10, 20)
            };

            long overlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 5,
                rangeEnd: 25,
                rangeIncomplete: true,
                out bool incomplete);

            Assert.That(incomplete, Is.True);
            Assert.That(overlap, Is.EqualTo(10));
        }

        [Test]
        public void TestStatusEvaluator_VerifiesTerminalStatesStrictly()
        {
            // 1. 0 案例應判定為失敗
            var emptyRes = TestStatusEvaluator.Evaluate(Array.Empty<TestStatusEvaluator.SimplifiedStatus>(), anyFixtureFailed: false);
            Assert.That(emptyRes.IsSuccess, Is.False);

            // 2. 全部 Passed 與 Skipped 應判定為成功
            var allPassRes = TestStatusEvaluator.Evaluate(new[]
            {
                TestStatusEvaluator.SimplifiedStatus.Passed,
                TestStatusEvaluator.SimplifiedStatus.Passed,
                TestStatusEvaluator.SimplifiedStatus.Skipped
            }, anyFixtureFailed: false);
            Assert.That(allPassRes.IsSuccess, Is.True);
            Assert.That(allPassRes.Total, Is.EqualTo(3));
            Assert.That(allPassRes.Passed, Is.EqualTo(2));
            Assert.That(allPassRes.Skipped, Is.EqualTo(1));

            // 3. 含 Failed / Canceled 應判定為失敗
            var failedRes = TestStatusEvaluator.Evaluate(new[]
            {
                TestStatusEvaluator.SimplifiedStatus.Passed,
                TestStatusEvaluator.SimplifiedStatus.Failed
            }, anyFixtureFailed: false);
            Assert.That(failedRes.IsSuccess, Is.False);

            // 4. 含 Pending / NotRun 未完成者應判定為失敗
            var pendingRes = TestStatusEvaluator.Evaluate(new[]
            {
                TestStatusEvaluator.SimplifiedStatus.Passed,
                TestStatusEvaluator.SimplifiedStatus.Pending
            }, anyFixtureFailed: false);
            Assert.That(pendingRes.IsSuccess, Is.False);

            // 5. Fixture 失敗即使所有 function passed 也判定為失敗
            var fixtureFailRes = TestStatusEvaluator.Evaluate(new[]
            {
                TestStatusEvaluator.SimplifiedStatus.Passed
            }, anyFixtureFailed: true);
            Assert.That(fixtureFailRes.IsSuccess, Is.False);
        }

        [Test]
        public void XmlBatchTracker_RestoresContextOnExceptionPath()
        {
            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.Null);

            XmlLoadingBatchTracker tracker = null;
            XmlLoadingBatchTracker prev = null;
            try
            {
                tracker = XmlLoadingBatchTracker.Enter("Mod.Exception", "Defs/", out prev);
                tracker.RecordScan(scanTicks: 10, expectedFiles: 5);
                throw new InvalidOperationException("simulated parse failure");
            }
            catch (InvalidOperationException)
            {
                // 預期例外
            }
            finally
            {
                if (tracker != null)
                {
                    XmlLoadingBatchTracker.Exit(tracker, prev);
                }
            }

            Assert.That(XmlLoadingBatchTracker.CurrentContext.Value, Is.Null, "Context must be restored even on exception path");
            Assert.That(tracker.WallEndTimestamp, Is.GreaterThan(0), "WallEndTimestamp must be set on Exit");
            Assert.That(tracker.ExpectedFiles, Is.EqualTo(5));
        }

        [Test]
        public void IntervalIntersection_UnstartedSoundRangeYieldsZeroOverlap()
        {
            // 音效預熱未啟動：rangeStart=0, rangeEnd=0 → 應回傳 0 且不標記 incomplete
            var intervals = new List<(long Start, long End)> { (10, 20) };

            long overlap = IntervalIntersectionHelper.CalculateOverlap(
                intervals,
                rangeStart: 0,
                rangeEnd: 0,
                rangeIncomplete: false,
                out bool incomplete);

            Assert.That(incomplete, Is.False);
            Assert.That(overlap, Is.EqualTo(0));
        }
    }
}
