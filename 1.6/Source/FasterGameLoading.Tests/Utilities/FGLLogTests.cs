using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.Utilities
{
    [TestFixture]
    public class FGLLogTests
    {
        private static Harmony harmony;
        private static readonly List<string> capturedMessages = new();
        private static readonly List<string> capturedWarnings = new();
        private static readonly List<string> capturedErrors = new();
        private static bool forceBackgroundThread;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.Utilities.FGLLogTests");

            var isInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), nameof(UnityData.IsInMainThread));
            if (isInMainThreadGetter != null)
            {
                harmony.Patch(isInMainThreadGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(FGLLogTests), nameof(MockIsInMainThread))));
            }

            var logMessageMethod = AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) });
            if (logMessageMethod != null)
            {
                harmony.Patch(logMessageMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(FGLLogTests), nameof(MockLogMessage))));
            }

            var logWarningMethod = AccessTools.Method(typeof(Log), nameof(Log.Warning), new[] { typeof(string) });
            if (logWarningMethod != null)
            {
                harmony.Patch(logWarningMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(FGLLogTests), nameof(MockLogWarning))));
            }

            var logErrorMethod = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
            if (logErrorMethod != null)
            {
                harmony.Patch(logErrorMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(FGLLogTests), nameof(MockLogError))));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.Utilities.FGLLogTests");
        }

        [SetUp]
        public void SetUp()
        {
            capturedMessages.Clear();
            capturedWarnings.Clear();
            capturedErrors.Clear();
            TestSetup.OnLogMessage = text => capturedMessages.Add(text);
            TestSetup.OnLogWarning = text => capturedWarnings.Add(text);
            TestSetup.OnLogError = text => capturedErrors.Add(text);
            TestSetup.IsInMainThreadOverride = () => !forceBackgroundThread;
            forceBackgroundThread = false;
            ClearPendingQueue();
        }

        [TearDown]
        public void TearDown()
        {
            TestSetup.OnLogMessage = null;
            TestSetup.OnLogWarning = null;
            TestSetup.OnLogError = null;
            TestSetup.IsInMainThreadOverride = null;
            ClearPendingQueue();
            forceBackgroundThread = false;
        }

        private static bool MockIsInMainThread(ref bool __result)
        {
            if (forceBackgroundThread)
            {
                __result = false;
                return false;
            }
            __result = true;
            return false;
        }

        private static bool MockLogMessage(string text)
        {
            capturedMessages.Add(text);
            return false;
        }

        private static bool MockLogWarning(string text)
        {
            capturedWarnings.Add(text);
            return false;
        }

        private static bool MockLogError(string text)
        {
            capturedErrors.Add(text);
            return false;
        }

        private static void ClearPendingQueue()
        {
            var pendingField = typeof(FGLLog).GetField("pending", BindingFlags.NonPublic | BindingFlags.Static);
            if (pendingField?.GetValue(null) is ConcurrentQueue<(Enum, string)> queue)
            {
                while (queue.TryDequeue(out _)) { }
            }
            else
            {
                // Fallback via FlushPending
                FGLLog.FlushPending();
                capturedMessages.Clear();
                capturedWarnings.Clear();
                capturedErrors.Clear();
            }
        }

        [Test]
        public void Message_WhenVerboseLoggingIsDisabled_IsSkipped()
        {
            var previous = FasterGameLoadingSettings.VerboseLogging;
            try
            {
                FasterGameLoadingSettings.VerboseLogging = false;

                FGLLog.Message("verbose message should be skipped");

                Assert.That(capturedMessages, Is.Empty);
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = previous;
            }
        }

        [Test]
        public void Message_WhenVerboseLoggingIsEnabled_EmitsMessageWithPrefix()
        {
            var previous = FasterGameLoadingSettings.VerboseLogging;
            try
            {
                FasterGameLoadingSettings.VerboseLogging = true;

                FGLLog.Message("test message");

                Assert.That(capturedMessages, Has.Count.EqualTo(1));
                Assert.That(capturedMessages[0], Is.EqualTo("[FasterGameLoading] test message"));
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = previous;
            }
        }

        [Test]
        public void Warning_And_Error_EmitWithPrefix()
        {
            FGLLog.Warning("warn text");
            FGLLog.Error("error text");

            Assert.That(capturedWarnings, Has.Count.EqualTo(1));
            Assert.That(capturedWarnings[0], Is.EqualTo("[FasterGameLoading] warn text"));

            Assert.That(capturedErrors, Has.Count.EqualTo(1));
            Assert.That(capturedErrors[0], Is.EqualTo("[FasterGameLoading] error text"));
        }

        [Test]
        public void Warning_And_Error_WithException_IncludeStackTrace()
        {
            var ex = new InvalidOperationException("Test exception message");

            FGLLog.Warning("warning with ex", ex);
            FGLLog.Error("error with ex", ex);

            Assert.That(capturedWarnings, Has.Count.EqualTo(1));
            Assert.That(capturedWarnings[0], Does.Contain("[FasterGameLoading] warning with ex"));
            Assert.That(capturedWarnings[0], Does.Contain("Test exception message"));

            Assert.That(capturedErrors, Has.Count.EqualTo(1));
            Assert.That(capturedErrors[0], Does.Contain("[FasterGameLoading] error with ex"));
            Assert.That(capturedErrors[0], Does.Contain("Test exception message"));
        }

        [Test]
        public void Logging_OnBackgroundThread_EnqueuesToPending_AndFlushesOnMainThread()
        {
            var previous = FasterGameLoadingSettings.VerboseLogging;
            try
            {
                FasterGameLoadingSettings.VerboseLogging = true;
                forceBackgroundThread = true;

                FGLLog.Message("bg message");
                FGLLog.Warning("bg warning");
                FGLLog.Error("bg error");

                // In background thread, logs should be queued, not directly written to Verse.Log
                Assert.That(capturedMessages, Is.Empty);
                Assert.That(capturedWarnings, Is.Empty);
                Assert.That(capturedErrors, Is.Empty);

                // Switch back to main thread and flush
                forceBackgroundThread = false;
                FGLLog.FlushPending();

                Assert.That(capturedMessages, Has.Count.EqualTo(1));
                Assert.That(capturedMessages[0], Is.EqualTo("[FasterGameLoading] bg message"));

                Assert.That(capturedWarnings, Has.Count.EqualTo(1));
                Assert.That(capturedWarnings[0], Is.EqualTo("[FasterGameLoading] bg warning"));

                Assert.That(capturedErrors, Has.Count.EqualTo(1));
                Assert.That(capturedErrors[0], Is.EqualTo("[FasterGameLoading] bg error"));
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = previous;
            }
        }

        [Test]
        public void FlushPending_WithEmptyQueueDoesNotThrow()
        {
            Assert.DoesNotThrow(() => FGLLog.FlushPending());
        }
    }
}
