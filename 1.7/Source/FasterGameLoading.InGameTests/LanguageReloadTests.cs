using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using DevTools.Testing;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 切換語言會跑 ClearAllPlayData + LoadAllPlayData：所有 ModContentPack、Def 與貼圖重建，CallAll 與 FGL 的延遲管線再跑一次，
    /// 但 Mod 類別不會重建（FGL 建構子不再執行）。FGL 靠 SessionLifecycle 的 LanguageReloading 在 SelectLanguage 時清掉所有快取。
    /// 開發者在第 1 輪測試後從主選單切換至日文；探針保存重載前狀態，供第 2 輪測試比對。
    /// </summary>
    [HarmonyPatch(typeof(LanguageDatabase), nameof(LanguageDatabase.SelectLanguage))]
    internal static class LanguageReload
    {
        // 選 FGL 沒有翻譯的語言，讓第 2 輪驗證原版英文退回。
        private const string TargetLanguagePrefix = "Japanese";

        public static string FromLanguage { get; private set; }

        public static string ToLanguage { get; private set; }

        // 切換前 FGL 提早載入的次數；重載期間提早載入不應重啟。
        public static int EarlyLoadsBeforeReload { get; private set; }

        /// <summary>
        /// 切換前已存在的靜態圖集。原版重載不會清空 staticTextureAtlases（只清 buildQueue），
        /// 舊圖集會一直留著，而 Core 貼圖來自 Resources、重載後仍是同一批實體，因此新舊圖集必然重複；
        /// 第 2 輪的圖集檢查排除這些實體，只看本次載入烘焙的圖集（以參考比對，不假設清單只會往後附加）。
        /// </summary>
        public static HashSet<StaticTextureAtlas> AtlasesBeforeReload { get; } = new HashSet<StaticTextureAtlas>();

        public static void Prefix(LoadedLanguage __0)
        {
            if (FglState.Quicktest || !TestRunDriver.Ready || TestRunDriver.Round != 1
                || __0 == null || LanguageDatabase.activeLanguage == null
                || string.Equals(LanguageDatabase.activeLanguage.folderName, __0.folderName, StringComparison.Ordinal)
                || !__0.folderName.StartsWith(TargetLanguagePrefix, StringComparison.Ordinal))
                return;
            FromLanguage = LanguageDatabase.activeLanguage.folderName;
            ToLanguage = __0.folderName;
            lock (ContentLoadProbe.LoadedBeforePatches) EarlyLoadsBeforeReload = ContentLoadProbe.EarlyLoads;
            AtlasesBeforeReload.Clear();
            AtlasesBeforeReload.UnionWith(GlobalTextureAtlasManager.staticTextureAtlases);
            TestRunDriver.BeginReloadRound();
            Log.Message($"[FGL InGameTests] Tracking language reload {FromLanguage} -> {ToLanguage}.");
        }

        public static void Postfix(bool __runOriginal)
        {
            if (__runOriginal || TestRunDriver.Round != 2) return;
            TestRunDriver.CancelReloadRound();
            Log.Warning("[FGL InGameTests] Language switch was canceled; reload round skipped.");
        }
    }

    // 只在第 2 輪（語言重載後）才有意義的檢查；第 1 輪直接略過。
    [TestFixture(TestType.MainMenu)]
    internal class LanguageReloadTests
    {
        public LanguageReloadTests() { }

        private static bool AfterReload => TestRunDriver.Round == 2;

        [Test]
        public static void LanguageWasSwitched()
        {
            if (!AfterReload) return;
            Expect.AreEqual(LanguageDatabase.activeLanguage.folderName, LanguageReload.ToLanguage, "active language does not match the reload target");
        }

        // 第 2 輪的語言沒有 FGL 翻譯資料夾，TranslationTests 才真的走到原版英文退回。
        [Test]
        public static void ReloadRoundExercisesEnglishFallback()
        {
            if (!AfterReload) return;
            var candidate = Path.Combine(FasterGameLoadingMod.Instance.Content.RootDir, "1.7", "LanguageData", LanguageDatabase.activeLanguage.folderName);
            Expect.IsFalse(Directory.Exists(candidate), "FGL unexpectedly provides translations for the reload language");
        }

        // 重載時 RunningMods 是一批新的 ModContentPack；FGL 不重啟提早載入，全部交給原版流程。
        [Test]
        public static void EarlyLoadingDidNotRestart()
        {
            if (!AfterReload) return;
            lock (ContentLoadProbe.LoadedBeforePatches)
            {
                Expect.AreEqual(ContentLoadProbe.EarlyLoads, LanguageReload.EarlyLoadsBeforeReload, "early loading restarted during language reload");
            }
        }

        // LanguageReloading 沒清掉的話，舊 ModContentPack／handler 會留在集合裡，新的同名 mod 則可能被誤判為已載入。
        [Test]
        public static void ContentTrackingOnlyHoldsCurrentModPacks()
        {
            if (!AfterReload) return;
            var running = LoadedModManager.RunningMods.ToHashSet();
            var handlers = running.Select(static m => m.assetBundles).ToHashSet();
            var failures = ModContentPack_ReloadContentInt_Patch.loadedMods
                .Where(m => !running.Contains(m))
                .Select(static m => $"stale content pack {m.PackageIdPlayerFacing}")
                .Concat(ModAssetBundlesHandler_ReloadAll_Patch.reloadedHandlers
                    .Where(h => !handlers.Contains(h))
                    .Select(static _ => "stale asset bundle handler"))
                .ToList();
            FglState.AssertNone(failures, "entries from before the reload");
        }

        // 重載會銷毀所有舊貼圖；FGL 的貼圖快取若還留著它們，之後命中就會拿到已銷毀的貼圖。
        [Test]
        public static void TextureCacheHoldsNoDestroyedTextures()
        {
            if (!AfterReload) return;
            var failures = LoadedTextureRegistry.Snapshot()
                .Where(static e => e.Key == null)
                .Select(static e => e.Value)
                .ToList();
            FglState.AssertNone(failures, "destroyed textures still cached");
        }

        [Test]
        public static void EarlyLoadGateReopenedAfterReload()
        {
            if (!AfterReload) return;
            Expect.IsTrue(EarlyModContentLoader.ModClassesCreated, "early-load gate did not reopen after language reload");
        }
    }
}
