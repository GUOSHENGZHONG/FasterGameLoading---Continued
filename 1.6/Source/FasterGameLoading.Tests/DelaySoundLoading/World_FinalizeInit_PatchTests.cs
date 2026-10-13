using System;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace FasterGameLoading.Tests.DelaySoundLoading
{
    [TestFixture]
    public class World_FinalizeInit_PatchTests
    {
        private static Harmony harmony;
        private DelayedActions delayedActions;
        private static Action capturedExecuteWhenFinishedAction;

        private static bool PrefixSkip() => false;

        private static bool MockExecuteWhenFinished(Action action)
        {
            capturedExecuteWhenFinishedAction = action;
            return false;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.World_FinalizeInit_PatchTests");

            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(World_FinalizeInit_PatchTests), nameof(PrefixSkip))));
            }

            // Patch LongEventHandler.ExecuteWhenFinished (may fail if ECall, guard with try/catch)
            try
            {
                var execWhenFinished = AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished));
                if (execWhenFinished != null)
                {
                    harmony.Patch(execWhenFinished, prefix: new HarmonyMethod(AccessTools.Method(typeof(World_FinalizeInit_PatchTests), nameof(MockExecuteWhenFinished))));
                }
            }
            catch { }

            var fglModHarmonyProp = typeof(FasterGameLoadingMod).GetProperty(
                nameof(FasterGameLoadingMod.harmony), BindingFlags.Public | BindingFlags.Static);
            fglModHarmonyProp?.SetValue(null, new Harmony("FasterGameLoadingMod.WorldFinalizeTestInstance"), index: null);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.World_FinalizeInit_PatchTests");
        }

        [SetUp]
        public void SetUp()
        {
            delayedActions = new DelayedActions();
            capturedExecuteWhenFinishedAction = null;
            SoundStarter_Patch.ResetUnpatchedStatus();

            var prop = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.delayedActions), BindingFlags.Public | BindingFlags.Static);
            prop?.SetValue(null, delayedActions, index: null);
        }

        [TearDown]
        public void TearDown()
        {
            delayedActions?.ClearQueues();
            SoundStarter_Patch.ResetUnpatchedStatus();
            var prop = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.delayedActions), BindingFlags.Public | BindingFlags.Static);
            prop?.SetValue(null, value: null, index: null);
        }

        [Test]
        public void Patch_IsRegisteredForWorldFinalizeInit()
        {
            var patch = (HarmonyPatch)System.Attribute.GetCustomAttribute(
                typeof(World_FinalizeInit_Patch), typeof(HarmonyPatch));

            Assert.That(patch, Is.Not.Null);
            Assert.That(typeof(World_FinalizeInit_Patch).GetMethod("Postfix"), Is.Not.Null);
        }

        [Test]
        public void Postfix_EnqueuesExecuteWhenFinished_DrainsSubSounds_AndUnpatchesSoundStarter()
        {
            var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool sound1Executed = false;
            bool sound2Executed = false;

            delayedActions.EnqueueSubSound(sound1, () => sound1Executed = true);
            delayedActions.EnqueueSubSound(sound2, () => sound2Executed = true);

            World_FinalizeInit_Patch.Postfix();

            Assert.That(capturedExecuteWhenFinishedAction, Is.Not.Null);

            var unpatchedField = AccessTools.Field(typeof(SoundStarter_Patch), "unpatched");
            Assert.That((bool)unpatchedField.GetValue(null), Is.False);

            capturedExecuteWhenFinishedAction();

            Assert.That(sound1Executed, Is.True);
            Assert.That(sound2Executed, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
            Assert.That((bool)unpatchedField.GetValue(null), Is.True);
        }

        [Test]
        public void Postfix_WhenSubSoundActionThrows_CatchesAndContinuesDrainingAndUnpatches()
        {
            var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
            bool sound2Executed = false;

            delayedActions.EnqueueSubSound(sound1, () => throw new InvalidOperationException("Failed audio grain"));
            delayedActions.EnqueueSubSound(sound2, () => sound2Executed = true);

            World_FinalizeInit_Patch.Postfix();

            Assert.That(capturedExecuteWhenFinishedAction, Is.Not.Null);

            var unpatchedField = AccessTools.Field(typeof(SoundStarter_Patch), "unpatched");

            Assert.DoesNotThrow(() => capturedExecuteWhenFinishedAction());

            Assert.That(sound2Executed, Is.True);
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
            Assert.That((bool)unpatchedField.GetValue(null), Is.True);
        }
    }
}
