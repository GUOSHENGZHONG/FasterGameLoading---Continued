using System.Collections;
using DevTools.Testing;
using UnityEngine;
using Verse;

namespace FasterGameLoading.InGameTests
{
    // DevTools 在主選單可見後就可能執行測試；FGL 的延遲管線仍需逐幀完成。
    [TestFixture(TestType.MainMenu)]
    [ExecutionPriority(Priority.First)]
    internal sealed class MainMenuReadinessTests
    {
        public MainMenuReadinessTests() { }

        [Test]
        [ExecutionPriority(Priority.First)]
        public IEnumerator WaitForDeferredPipeline() => ReadinessGate.Wait(requireMap: false);
    }

    // quicktest 需等地圖建立及 World.FinalizeInit 路徑完成。
    [TestFixture(TestType.Playing)]
    [ExecutionPriority(Priority.First)]
    internal sealed class PlayingReadinessTests
    {
        public PlayingReadinessTests() { }

        [Test]
        [ExecutionPriority(Priority.First)]
        public IEnumerator WaitForDeferredPipeline() => ReadinessGate.Wait(requireMap: true);
    }

    internal static class ReadinessGate
    {
        private const float TimeoutSeconds = 300f;

        public static IEnumerator Wait(bool requireMap)
        {
            float started = Time.realtimeSinceStartup;
            while (!IsReady() && Time.realtimeSinceStartup - started < TimeoutSeconds)
            {
                yield return null;
            }
            if (IsReady())
            {
                Expect.IsTrue(true);
                yield break;
            }

            Test.Fail($"FGL deferred pipeline or map did not finish within {TimeoutSeconds} seconds (round {TestRunDriver.Round}).");
            TestRunner.Current?.SignalToStop();

            bool IsReady() => TestRunDriver.Ready
                && FglState.DeferredPipelineFinished(TestRunDriver.Round)
                && (!requireMap || Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null);
        }
    }
}
