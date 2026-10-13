using System;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class BuildableDef_PostLoad_PatchTests
    {
        private static Harmony harmony;
        private static Action capturedExecuteWhenFinishedAction;
        private DelayedActions delayedActions;

        private static bool MockExecuteWhenFinished(Action action)
        {
            capturedExecuteWhenFinishedAction = action;
            return false;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.BuildableDef_PostLoad_PatchTests");
            harmony.Patch(
                AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(BuildableDef_PostLoad_PatchTests), nameof(MockExecuteWhenFinished))));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.BuildableDef_PostLoad_PatchTests");
        }

        [SetUp]
        public void SetUp()
        {
            capturedExecuteWhenFinishedAction = null;
            delayedActions = new DelayedActions();
            SetDelayedActions(delayedActions);
        }

        [TearDown]
        public void TearDown()
        {
            delayedActions?.ClearQueues();
            SetDelayedActions(null);
        }

        private static void SetDelayedActions(DelayedActions value)
        {
            typeof(FasterGameLoadingMod)
                .GetProperty(nameof(FasterGameLoadingMod.delayedActions), BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, value, index: null);
        }

        [Test]
        public void Prepare_ReflectsDelayGraphicLoadingSetting()
        {
            var previous = FasterGameLoadingSettings.DelayGraphicLoading;
            try
            {
                FasterGameLoadingSettings.DelayGraphicLoading = true;
                Assert.That(BuildableDef_PostLoad_Patch.Prepare(), Is.True);

                FasterGameLoadingSettings.DelayGraphicLoading = false;
                Assert.That(BuildableDef_PostLoad_Patch.Prepare(), Is.False);
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = previous;
            }
        }

        [Test]
        public void ExecuteDelayed_ExposesActionAndDefParameters()
        {
            var method = typeof(BuildableDef_PostLoad_Patch).GetMethod(
                nameof(BuildableDef_PostLoad_Patch.ExecuteDelayed));

            Assert.That(method, Is.Not.Null);
            Assert.That(method.GetParameters(), Has.Length.EqualTo(2));
        }

        [Test]
        public void ExecuteDelayed_WhenDelayedActionsIsNull_RunsActionInExecuteWhenFinished()
        {
            SetDelayedActions(null);
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            bool actionExecuted = false;

            BuildableDef_PostLoad_Patch.ExecuteDelayed(() => actionExecuted = true, def);
            capturedExecuteWhenFinishedAction();

            Assert.That(actionExecuted, Is.True);
        }

        [Test]
        public void ExecuteDelayed_WhenNotLoadedImmediately_EnqueuesIconInCallback()
        {
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            bool actionExecuted = false;

            BuildableDef_PostLoad_Patch.ExecuteDelayed(() => actionExecuted = true, def);
            Assert.That(delayedActions.IconsToLoadCount, Is.Zero, "PostLoad 當下只排程，不決定延遲與否。");

            capturedExecuteWhenFinishedAction();

            Assert.That(actionExecuted, Is.False);
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(1));
        }

        [Test]
        public void ExecuteDelayed_DecidesOnlyAfterCrossReferencesAreResolved()
        {
            // PostLoad 當下交叉參照尚未解析，designationCategory 仍是 null。
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            bool actionExecuted = false;

            BuildableDef_PostLoad_Patch.ExecuteDelayed(() => actionExecuted = true, def);
            Assert.That(delayedActions.IconsToLoadCount, Is.Zero, "不得在 PostLoad 當下就決定延遲。");

            def.designationCategory = (DesignationCategoryDef)FormatterServices.GetUninitializedObject(typeof(DesignationCategoryDef));
            capturedExecuteWhenFinishedAction();

            Assert.That(actionExecuted, Is.True, "有 designationCategory 的建築必須立即解析圖示。");
            Assert.That(delayedActions.IconsToLoadCount, Is.Zero);
        }
    }
}
