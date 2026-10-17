using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 開發測試附屬模組的進入點。DevTools 負責探索及執行 fixture；
    /// 本模組在 FGL 建構前安裝探針，並由 <see cref="TestRunDriver"/> 回報延遲管線完成時機。
    /// </summary>
    public sealed class InGameTestsMod : Mod
    {
        public InGameTestsMod(ModContentPack content) : base(content)
        {
            ApplySeededFglSettings(content);
            new Harmony(content.PackageId).PatchAll(typeof(InGameTestsMod).Assembly);
        }

        /// <summary>
        /// rimworld-mod-mcp 的 seed_config 會把 Mod_*_FasterGameLoadingMod.xml 改名成受測 mod（本 mod）的資料夾，
        /// FGL 因此讀不到。本 mod 排在 FGL 之前建構，趁 FGL 讀設定前把檔案複製回 FGL 的檔名。
        /// </summary>
        private static void ApplySeededFglSettings(ModContentPack content)
        {
            var seeded = LoadedModManager.GetSettingsFilename(content.FolderName, nameof(FasterGameLoadingMod));
            if (!File.Exists(seeded)) return;

            var fgl = LoadedModManager.RunningMods.FirstOrDefault(static m => string.Equals(m.PackageIdPlayerFacing, "Taranchuk.FasterGameLoading", StringComparison.OrdinalIgnoreCase));
            if (fgl == null)
            {
                Log.Error("[FGL InGameTests] Seeded FGL settings found but Taranchuk.FasterGameLoading is not running; settings not applied.");
                return;
            }
            // CLI 開發測試可能暫時把兩個組件放在同一個 mod；此時來源就是 FGL 自己的設定檔。
            if (ReferenceEquals(fgl, content)) return;
            File.Copy(seeded, LoadedModManager.GetSettingsFilename(fgl.FolderName, nameof(FasterGameLoadingMod)), overwrite: true);
            Log.Message($"[FGL InGameTests] Applied seeded FGL settings from {Path.GetFileName(seeded)}");
        }
    }

    /// <summary>
    /// 追蹤 DevTools 測試可執行的時機。第 1 輪為初次載入；若從主選單切換語言，
    /// 第 2 輪為重載後。quicktest 只在進入地圖後就緒。
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class TestRunDriver
    {
        // 等待延遲管線的上限；逾時仍會執行測試，讓未完成的狀態以測試失敗呈現。
        private const double TimeoutSeconds = 300;

        // 目前等待中（或正在執行）的輪次，從 1 開始。
        public static int Round { get; private set; } = 1;

        public static bool Ready { get; private set; }
        private static Stopwatch waitingSince;

        public static void BeginReloadRound()
        {
            Round = 2;
            Ready = false;
            waitingSince = null;
        }

        public static void CancelReloadRound()
        {
            Round = 1;
            Ready = true;
            waitingSince = null;
        }

        public static void Postfix()
        {
            if (Ready || !ReadyForRound())
            {
                return;
            }
            waitingSince ??= Stopwatch.StartNew();

            bool timedOut = waitingSince.Elapsed.TotalSeconds >= TimeoutSeconds;
            if (!timedOut && !FglState.DeferredPipelineFinished(Round))
            {
                return;
            }

            if (timedOut)
            {
                Log.Error($"[FGL InGameTests] Round {Round}: timed out after {TimeoutSeconds}s waiting for FGL's deferred pipeline.");
            }
            Log.Message($"[FGL InGameTests] Round {Round}: ready for DevTools tests after {waitingSince.Elapsed.TotalSeconds:F1}s wait. Language: {LanguageDatabase.activeLanguage?.folderName}, quicktest: {FglState.Quicktest}. Settings: {FglState.SettingsProfile}");
            // 比較不同版本的啟動速度用：自遊戲啟動起算的秒數，減掉上面的等待即為 PlayData 載入完成的時間點。
            Log.Message($"[FGL InGameTests] Round {Round} timing: realtimeSinceStartup={UnityEngine.Time.realtimeSinceStartup:F1}s, gcCollections={GC.CollectionCount(0)}, {MainThreadFileReadProbe.Summary}, {TypeEnumerationCost.Measure()}");
            waitingSince = null;
            Ready = true;
        }

        private static bool ReadyForRound()
        {
            if (!PlayDataLoader.Loaded || LongEventHandler.AnyEventNowOrWaiting || Find.UIRoot == null)
            {
                return false;
            }
            // quicktest 直接生成地圖：等地圖進入遊戲，World.FinalizeInit 那條音效路徑才會走過。
            return !FglState.Quicktest || (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null);
        }
    }
}
