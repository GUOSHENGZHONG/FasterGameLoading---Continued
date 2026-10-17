using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading;
[TestFixture]
public class DelayedActionsStageTests
{
    private static Harmony harmony;
    private static Texture2D mockBadTex;
    private DelayedActions delayedActions;

    private static bool PrefixSkip() => false;
    private static bool mockIsInMainThread = true;
    private static bool MockIsInMainThread(ref bool __result)
    {
        __result = mockIsInMainThread;
        return false;
    }

    private static ThingDef postLoadSpecialCalledDef;

    private static bool MockPostLoadSpecial(ThingDef parentDef)
    {
        postLoadSpecialCalledDef = parentDef;
        return false;
    }

    private static ThingDef CreateMockThingDef(string name)
    {
        var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        def.defName = name;
        return def;
    }

    [OneTimeSetUp]
#pragma warning disable MA0051 // 需依序初始化多個 Harmony mock patch，拆分成多個方法會降低可讀性
    public void OneTimeSetUp()
    {
        harmony = new Harmony("FasterGameLoading.Tests.DelayedActionsStageTests");

        mockBadTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        // 使用 BaseContent.BadTex 的真實值（可能是 null 或實際紋理）。
        // 不可嘗試覆寫 BadTex：它是 static readonly（initonly）欄位，型別初始化後 SetValue 會拋
        // FieldAccessException（cctor 已被 TestSetup 攔截，BadTex 保持未初始化）。
        // 直接讀取可確保 def.uiIcon 與 BaseContent.BadTex 永遠同一實例（ReferenceEquals 成立）。
        try
        {
            mockBadTex = BaseContent.BadTex;
        }
        catch
        {
            mockBadTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        }

        // Patch UnityData.IsInMainThread to return true
        var isInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), nameof(UnityData.IsInMainThread));
        if (isInMainThreadGetter != null)
        {
            harmony.Patch(isInMainThreadGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockIsInMainThread))));
        }

        // Patch FGLLog.Emit
        var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
        if (emitMethod != null)
        {
            harmony.Patch(emitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(PrefixSkip))));
        }

        // Patch PlantProperties.PostLoadSpecial
        var plantPropType = AccessTools.TypeByName("Verse.PlantProperties") ?? AccessTools.TypeByName("RimWorld.PlantProperties");
        var postLoadSpecialMethod = plantPropType != null ? AccessTools.Method(plantPropType, "PostLoadSpecial") : null;
        if (postLoadSpecialMethod != null)
        {
            harmony.Patch(postLoadSpecialMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockPostLoadSpecial))));
        }

        // Initialize FasterGameLoadingMod.harmony for SoundStarter_Patch.Unpatch()
        var fglModHarmonyProp = typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.harmony), BindingFlags.Public | BindingFlags.Static);
        fglModHarmonyProp?.SetValue(obj: null, value: new Harmony("FasterGameLoadingMod.TestInstance"), index: null);
    }
#pragma warning restore MA0051

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        harmony?.UnpatchAll("FasterGameLoading.Tests.DelayedActionsStageTests");
    }

    [SetUp]
    public void SetUp()
    {
        delayedActions = new DelayedActions();
        postLoadSpecialCalledDef = null;
        SoundStarter_Patch.ResetUnpatchedStatus();
    }

    [TearDown]
    public void TearDown()
    {
        delayedActions?.ClearQueues();
    }

    [Test]
    public void LoadDeferredGraphicsCoroutine_DrainsQueueAndPopulatesLoadedDefs()
    {
        var def1 = CreateMockThingDef("Def1");
        var def2 = CreateMockThingDef("Def2");
        bool act1Executed = false;
        bool act2Executed = false;

        delayedActions.EnqueueGraphic(def1, () => act1Executed = true);
        delayedActions.EnqueueGraphic(def2, () => act2Executed = true);

        var loadedDefs = new List<ThingDef>();
        var coroutine = delayedActions.LoadDeferredGraphicsCoroutine(loadedDefs);

        while (coroutine.MoveNext()) { }

        Assert.That(act1Executed, Is.True);
        Assert.That(act2Executed, Is.True);
        Assert.That(loadedDefs, Contains.Item(def1));
        Assert.That(loadedDefs, Contains.Item(def2));
        Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
    }

    [Test]
    public void DeferredIconAction_StillRunsAfterDeferredGraphicLoads()
    {
        var def = CreateMockThingDef("DefWithIconPath");
        def.uiIcon = mockBadTex;
        def.uiIconPath = "Things/Item/TestIcon";
        bool iconActionExecuted = false;
        delayedActions.EnqueueGraphic(def, () => { });
        delayedActions.EnqueueIcon(def, () => iconActionExecuted = true);

        var graphics = delayedActions.LoadDeferredGraphicsCoroutine(new List<ThingDef>());
        while (graphics.MoveNext()) { }
        var icons = delayedActions.LoadDeferredIconsCoroutine();
        while (icons.MoveNext()) { }

        // 原版圖示回呼（ResolveIcon）還會設定 uiIconColor、uiIconMaterial、uiIconAngle；
        // 圖形步驟若先自行填上 uiIcon，圖示步驟會以為已經補上而整個略過。
        Assert.That(iconActionExecuted, Is.True);
    }

    [Test]
    public void LoadDeferredGraphicsCoroutine_WhenActionThrows_CatchesAndContinuesWithRemainingDefs()
    {
        var def1 = CreateMockThingDef("Def1");
        var def2 = CreateMockThingDef("Def2_Fails");
        var def3 = CreateMockThingDef("Def3");
        bool act1Executed = false;
        bool act3Executed = false;

        delayedActions.EnqueueGraphic(def1, () => act1Executed = true);
        delayedActions.EnqueueGraphic(def2, () => throw new InvalidOperationException("Simulated load error"));
        delayedActions.EnqueueGraphic(def3, () => act3Executed = true);

        var loadedDefs = new List<ThingDef>();
        var coroutine = delayedActions.LoadDeferredGraphicsCoroutine(loadedDefs);

        Assert.DoesNotThrow(() =>
        {
            while (coroutine.MoveNext()) { }
        });

        Assert.That(act1Executed, Is.True);
        Assert.That(act3Executed, Is.True);
        Assert.That(loadedDefs, Contains.Item(def1));
        Assert.That(loadedDefs, Does.Not.Contain(def2));
        Assert.That(loadedDefs, Contains.Item(def3));
        Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(0));
    }

    [Test]
    public void LoadDeferredGraphicsCoroutine_CallsPlantPostLoadSpecialOnSuccess_AndSkipsOnFailure()
    {
        var plantPropType = AccessTools.TypeByName("Verse.PlantProperties") ?? AccessTools.TypeByName("RimWorld.PlantProperties");
        var plantPropObj = plantPropType != null ? FormatterServices.GetUninitializedObject(plantPropType) : null;

        var defSuccess = CreateMockThingDef("DefPlantSuccess");
        var plantField = AccessTools.Field(typeof(ThingDef), "plant");
        plantField?.SetValue(defSuccess, plantPropObj);

        delayedActions.EnqueueGraphic(defSuccess, () => { });

        var loadedDefs = new List<ThingDef>();
        var coroutine = delayedActions.LoadDeferredGraphicsCoroutine(loadedDefs);

        while (coroutine.MoveNext()) { }

        Assert.That(postLoadSpecialCalledDef, Is.SameAs(defSuccess));

        // Now test failure doesn't invoke PostLoadSpecial
        postLoadSpecialCalledDef = null;
        var defFailure = CreateMockThingDef("DefPlantFailure");
        plantField?.SetValue(defFailure, plantPropObj);

        delayedActions.EnqueueGraphic(defFailure, () => throw new InvalidOperationException("Failed"));
        coroutine = delayedActions.LoadDeferredGraphicsCoroutine(loadedDefs);

        while (coroutine.MoveNext()) { }

        Assert.That(postLoadSpecialCalledDef, Is.Null);
    }

    [Test]
    public void LoadDeferredIconsCoroutine_ExecutesOnlyWhenUiIconIsBadTex()
    {
        var defBadTex = CreateMockThingDef("DefBadTex");
        defBadTex.uiIcon = mockBadTex;

        var validTex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        var defGoodTex = CreateMockThingDef("DefGoodTex");
        defGoodTex.uiIcon = validTex;

        bool badTexActionExecuted = false;
        bool goodTexActionExecuted = false;

        delayedActions.EnqueueIcon(defBadTex, () => badTexActionExecuted = true);
        delayedActions.EnqueueIcon(defGoodTex, () => goodTexActionExecuted = true);

        var coroutine = delayedActions.LoadDeferredIconsCoroutine();
        while (coroutine.MoveNext()) { }

        Assert.That(badTexActionExecuted, Is.True);
        Assert.That(goodTexActionExecuted, Is.False);
        Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
    }

    [Test]
    public void LoadDeferredIconsCoroutine_WhenActionThrows_CatchesAndContinues()
    {
        var def1 = CreateMockThingDef("DefIcon1");
        def1.uiIcon = mockBadTex;
        var def2 = CreateMockThingDef("DefIcon2");
        def2.uiIcon = mockBadTex;

        bool def2Executed = false;
        delayedActions.EnqueueIcon(def1, () => throw new InvalidOperationException("Icon error"));
        delayedActions.EnqueueIcon(def2, () => def2Executed = true);

        var coroutine = delayedActions.LoadDeferredIconsCoroutine();
        Assert.DoesNotThrow(() =>
        {
            while (coroutine.MoveNext()) { }
        });

        Assert.That(def2Executed, Is.True);
        Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(0));
    }

    [Test]
    public void ResolveSubSoundDefsCoroutine_DrainsQueueAndCallsUnpatch()
    {
        var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        bool act1Executed = false;
        bool act2Executed = false;

        delayedActions.EnqueueSubSound(sound1, () => act1Executed = true);
        delayedActions.EnqueueSubSound(sound2, () => act2Executed = true);

        var coroutine = delayedActions.ResolveSubSoundDefsCoroutine();
        while (coroutine.MoveNext()) { }

        Assert.That(act1Executed, Is.True);
        Assert.That(act2Executed, Is.True);
        Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
    }

    [Test]
    public void ResolveSubSoundDefsCoroutine_WhenActionThrows_CatchesAndContinues()
    {
        var sound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        var sound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        bool act2Executed = false;

        delayedActions.EnqueueSubSound(sound1, () => throw new InvalidOperationException("Audio error"));
        delayedActions.EnqueueSubSound(sound2, () => act2Executed = true);

        var coroutine = delayedActions.ResolveSubSoundDefsCoroutine();
        Assert.DoesNotThrow(() =>
        {
            while (coroutine.MoveNext()) { }
        });

        Assert.That(act2Executed, Is.True);
        Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(0));
    }

    [Test]
    public void UpdateMapMeshForLoadedDefs_WhenCurrentGameNull_RunsSafely()
    {
        Assert.DoesNotThrow(() => DelayedActions.UpdateMapMeshForLoadedDefs(new List<ThingDef>()));
    }

    [Test]
    public void LoadDeferredGraphicsCoroutine_WhenOverBudget_YieldsAndRestartsStopwatch()
    {
        var def1 = CreateMockThingDef("TestDef1");
        var def2 = CreateMockThingDef("TestDef2");

        delayedActions.EnqueueGraphic(def1, () => { });
        delayedActions.EnqueueGraphic(def2, () => { });

        var getter = AccessTools.PropertyGetter(typeof(DelayedActions), nameof(DelayedActions.IsOverBudget));
        var patch = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockOverBudgetTrue)));
        harmony.Patch(getter, prefix: patch);

        try
        {
            var coroutine = delayedActions.LoadDeferredGraphicsCoroutine(new List<ThingDef>());
            bool moved = coroutine.MoveNext();
            Assert.That(moved, Is.True);
            Assert.That(coroutine.Current, Is.EqualTo(0));
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(2));
        }
        finally
        {
            harmony.Unpatch(getter, patch.method);
        }
    }

    private static bool MockOverBudgetTrue(ref bool __result)
    {
        __result = true;
        return false;
    }

    [Test]
    public void LoadDeferredIconsCoroutine_WhenNotInMainThread_YieldsAndContinues()
    {
        var iconDef = (BuildableDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        delayedActions.EnqueueIcon(iconDef, () => { });

        mockIsInMainThread = false;
        TestSetup.IsInMainThreadOverride = () => false;
        try
        {
            var coroutine = delayedActions.LoadDeferredIconsCoroutine();
            bool moved = coroutine.MoveNext();

            Assert.That(moved, Is.True);
            Assert.That(coroutine.Current, Is.EqualTo(0));
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(1));
        }
        finally
        {
            mockIsInMainThread = true;
            TestSetup.IsInMainThreadOverride = null;
        }
    }

    [Test]
    public void LoadDeferredGraphicsCoroutine_WhenNotInMainThread_YieldsAndContinues()
    {
        var def = CreateMockThingDef("TestDef");
        delayedActions.EnqueueGraphic(def, () => { });

        mockIsInMainThread = false;
        TestSetup.IsInMainThreadOverride = () => false;
        try
        {
            var coroutine = delayedActions.LoadDeferredGraphicsCoroutine(new List<ThingDef>());
            bool moved = coroutine.MoveNext();

            Assert.That(moved, Is.True);
            Assert.That(coroutine.Current, Is.EqualTo(0));
            Assert.That(delayedActions.GraphicsToLoadCount, Is.EqualTo(1));
        }
        finally
        {
            mockIsInMainThread = true;
            TestSetup.IsInMainThreadOverride = null;
        }
    }

    [Test]
    public void LoadDeferredIconsCoroutine_WhenOverBudget_YieldsAndRestartsStopwatch()
    {
        var iconDef1 = (BuildableDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        var iconDef2 = (BuildableDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        delayedActions.EnqueueIcon(iconDef1, () => { });
        delayedActions.EnqueueIcon(iconDef2, () => { });

        var getter = AccessTools.PropertyGetter(typeof(DelayedActions), nameof(DelayedActions.IsOverBudget));
        var patch = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockOverBudgetTrue)));
        harmony.Patch(getter, prefix: patch);

        try
        {
            var coroutine = delayedActions.LoadDeferredIconsCoroutine();
            bool moved = coroutine.MoveNext();
            Assert.That(moved, Is.True);
            Assert.That(coroutine.Current, Is.EqualTo(0));
            Assert.That(delayedActions.IconsToLoadCount, Is.EqualTo(2));
        }
        finally
        {
            harmony.Unpatch(getter, patch.method);
        }
    }

    [Test]
    public void ResolveSubSoundDefsCoroutine_WhenOverBudget_YieldsAndRestartsStopwatch()
    {
        var subSound1 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        var subSound2 = (SubSoundDef)FormatterServices.GetUninitializedObject(typeof(SubSoundDef));
        delayedActions.EnqueueSubSound(subSound1, () => { });
        delayedActions.EnqueueSubSound(subSound2, () => { });

        var getter = AccessTools.PropertyGetter(typeof(DelayedActions), nameof(DelayedActions.IsOverBudget));
        var patch = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockOverBudgetTrue)));
        harmony.Patch(getter, prefix: patch);

        try
        {
            var coroutine = delayedActions.ResolveSubSoundDefsCoroutine();
            bool moved = coroutine.MoveNext();
            Assert.That(moved, Is.True);
            Assert.That(coroutine.Current, Is.EqualTo(0));
            Assert.That(delayedActions.SubSoundDefToResolveCount, Is.EqualTo(2));
        }
        finally
        {
            harmony.Unpatch(getter, patch.method);
        }
    }

    private static bool MockCurrentGame(ref Game __result)
    {
        __result = (Game)FormatterServices.GetUninitializedObject(typeof(Game));
        return false;
    }

    private static bool MockFindMapsThrows(ref List<Map> __result)
    {
        throw new InvalidOperationException("Simulated map error");
    }

    [Test]
    public void UpdateMapMeshForLoadedDefs_WhenFindMapsThrows_CatchesAndLogsWarning()
    {
        var gameProp = AccessTools.PropertyGetter(typeof(Current), nameof(Current.Game));
        var mapsProp = AccessTools.PropertyGetter(typeof(Find), nameof(Find.Maps));

        var patchGame = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockCurrentGame)));
        var patchMaps = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockFindMapsThrows)));

        harmony.Patch(gameProp, prefix: patchGame);
        harmony.Patch(mapsProp, prefix: patchMaps);

        try
        {
            Assert.DoesNotThrow(() => DelayedActions.UpdateMapMeshForLoadedDefs([CreateMockThingDef("TestA")]));
        }
        finally
        {
            harmony.Unpatch(gameProp, patchGame.method);
            harmony.Unpatch(mapsProp, patchMaps.method);
        }
    }

    private static List<Map> mockedMaps;
    private static List<IntVec3> dirtyPositions;

    private static bool MockFindMaps(ref List<Map> __result)
    {
        __result = mockedMaps;
        return false;
    }

    private static bool RecordMapMeshDirty(ref IntVec3 loc, ulong dirtyFlags)
    {
        dirtyPositions?.Add(loc);
        return false;
    }

    /// <summary>
    /// 空的前置補丁，用來迫使執行期重新編譯 UpdateMapMeshForLoadedDefs。
    /// Current.Game 與 Find.Maps 都是會被內嵌的簡單取值函式；若受測方法早於補丁就被編譯，
    /// 內嵌的舊碼會讓測試的 mock 失效（這也是這段內圈過去沒有測試涵蓋的原因）。
    /// </summary>
    private static bool ForceMethodRecompile() => true;

    /// <summary>組出假地圖：只帶入 listerThings 的 def → 東西對照與空 MapDrawer，避免動到遊戲執行期狀態。</summary>
    private static Map CreateFakeMap(Dictionary<ThingDef, List<Thing>> thingsByDef)
    {
        var map = (Map)FormatterServices.GetUninitializedObject(typeof(Map));
        var drawer = (MapDrawer)FormatterServices.GetUninitializedObject(typeof(MapDrawer));
        // sections 與 listsByDef 不是公開成員，測試專案沒有 Publicizer，故以反射填入。
        AccessTools.Field(typeof(MapDrawer), "sections").SetValue(drawer, new Section[0, 0]);
        var lister = (ListerThings)FormatterServices.GetUninitializedObject(typeof(ListerThings));
        AccessTools.Field(typeof(ListerThings), "listsByDef").SetValue(lister, thingsByDef);
        map.mapDrawer = drawer;
        map.listerThings = lister;
        return map;
    }

    [Test]
    public void UpdateMapMeshForLoadedDefs_DirtiesOnlyThingsOfLoadedDefs()
    {
        var loadedDef = CreateMockThingDef("TestMeshLoaded");
        var missingListDef = CreateMockThingDef("TestMeshMissing");
        var otherDef = CreateMockThingDef("TestMeshOther");
        var loadedThing = (Thing)FormatterServices.GetUninitializedObject(typeof(Thing));
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(loadedThing, new IntVec3(3, 0, 7));
        var otherThing = (Thing)FormatterServices.GetUninitializedObject(typeof(Thing));
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(otherThing, new IntVec3(9, 0, 9));
        mockedMaps = [CreateFakeMap(new Dictionary<ThingDef, List<Thing>>
        {
            [loadedDef] = [loadedThing],
            [missingListDef] = null,
            [otherDef] = [otherThing],
        })];
        dirtyPositions = [];

        var gameProp = AccessTools.PropertyGetter(typeof(Current), nameof(Current.Game));
        var mapsProp = AccessTools.PropertyGetter(typeof(Find), nameof(Find.Maps));
        var meshMethod = AccessTools.Method(typeof(DelayedActions), nameof(DelayedActions.UpdateMapMeshForLoadedDefs));
        var dirty = AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.MapMeshDirty), [typeof(IntVec3), typeof(ulong)]);
        var patchGame = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockCurrentGame)));
        var patchMaps = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(MockFindMaps)));
        var patchRecompile = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(ForceMethodRecompile)));
        var patchDirty = new HarmonyMethod(AccessTools.Method(typeof(DelayedActionsStageTests), nameof(RecordMapMeshDirty)));
        harmony.Patch(gameProp, prefix: patchGame);
        harmony.Patch(mapsProp, prefix: patchMaps);
        harmony.Patch(meshMethod, prefix: patchRecompile);
        harmony.Patch(dirty, prefix: patchDirty);
        try
        {
            DelayedActions.UpdateMapMeshForLoadedDefs([loadedDef, missingListDef]);

            Assert.That(dirtyPositions, Has.Count.EqualTo(1),
                "只有已載入 def 的東西要重繪；lister 缺少該 def 或其他 def 都不受影響。");
            Assert.That(dirtyPositions[0], Is.EqualTo(new IntVec3(3, 0, 7)));
        }
        finally
        {
            harmony.Unpatch(gameProp, patchGame.method);
            harmony.Unpatch(mapsProp, patchMaps.method);
            harmony.Unpatch(meshMethod, patchRecompile.method);
            harmony.Unpatch(dirty, patchDirty.method);
            mockedMaps = null;
            dirtyPositions = null;
        }
    }
}
