using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking;
[TestFixture]
public class GlobalTextureAtlasManager_BakeStaticAtlases_PatchTests
{
    private static Harmony harmony;
    private bool previousDelay;
    private bool previousStaticBake;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        harmony = new Harmony("FasterGameLoading.Tests.AtlasPatch");
        var prefixSkip = new HarmonyMethod(AccessTools.Method(typeof(GlobalTextureAtlasManager_BakeStaticAtlases_PatchTests), nameof(PrefixSkip)));
        harmony.Patch(AccessTools.Method(typeof(FGLLog), "Emit"), prefix: prefixSkip);
        harmony.Patch(AccessTools.Method(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases)), prefix: prefixSkip);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        harmony.UnpatchAll("FasterGameLoading.Tests.AtlasPatch");
    }

    private static bool PrefixSkip() => false;

    [SetUp]
    public void SetUp()
    {
        previousDelay = FasterGameLoadingSettings.DelayGraphicLoading;
        previousStaticBake = FasterGameLoadingSettings.StaticAtlasesBaking;
        SessionLifecycle.Raise(LifecyclePhase.LanguageReloading);
    }

    [TearDown]
    public void TearDown()
    {
        FasterGameLoadingSettings.DelayGraphicLoading = previousDelay;
        FasterGameLoadingSettings.StaticAtlasesBaking = previousStaticBake;
        DelayedActions.ReleaseStartupSettingsForTests();
        SessionLifecycle.Raise(LifecyclePhase.LanguageReloading);
    }

    private static void CaptureSettings(bool delay)
    {
        FasterGameLoadingSettings.DelayGraphicLoading = delay;
        DelayedActions.CaptureStartupSettings();
    }

    private static void RunDeferredPipeline()
    {
        DelayGraphicAndIconLoading.DelayedActionsTests.RunLikeUnity(new DelayedActions().PerformActions());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Prefix_WithoutDeferredVisuals_LetsVanillaRun(bool adaptiveSetting)
    {
        // 同步路徑直接交給原版；1.7 不使用自適應烘焙，設定值不影響結果。
        FasterGameLoadingSettings.StaticAtlasesBaking = adaptiveSetting;
        CaptureSettings(delay: false);

        Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Prefix_WithDeferredVisuals_SkipsStartupBake(bool adaptiveSetting)
    {
        FasterGameLoadingSettings.StaticAtlasesBaking = adaptiveSetting;
        CaptureSettings(delay: true);

        Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Prefix_AfterDeferredPipeline_LetsVanillaRun(bool adaptiveSetting)
    {
        FasterGameLoadingSettings.StaticAtlasesBaking = adaptiveSetting;
        CaptureSettings(delay: true);

        RunDeferredPipeline();

        Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.True);
    }
}
