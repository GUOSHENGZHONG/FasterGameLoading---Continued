using System;
using System.Collections.Generic;
using HarmonyLib;
using RimTestRedux;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 多執行緒預載入改寫了 DirectXmlLoader.XmlAssetsInModFolder；檔案順序或載入資料夾覆蓋錯了，
    /// 後面的 Def 合併與 Patch 就會作用在錯的內容上，而且不會有任何錯誤訊息。
    /// 對每個 mod 的 Defs/ 與 Patches/，逐一與原版（reverse patch 的未改寫副本）比對結果。
    /// </summary>
    [TestSuite]
    internal static class XmlLoadingTests
    {
        /// <summary>與 ModContentPack.LoadDefs／LoadPatches 傳入的資料夾字串相同。</summary>
        private static readonly string[] Folders = { "Defs/", "Patches/" };

        [Test]
        public static void ParallelXmlAssetsMatchVanilla()
        {
            if (!FasterGameLoadingSettings.EnableMultiThreading) return;

            var failures = new List<string>();
            int compared = 0;
            foreach (var mod in LoadedModManager.RunningMods)
            {
                foreach (var folder in Folders)
                {
                    var fgl = DirectXmlLoader.XmlAssetsInModFolder(mod, folder);
                    var vanilla = VanillaXml.XmlAssetsInModFolder(mod, folder, null);
                    compared += vanilla.Length;
                    Compare(mod, folder, fgl, vanilla, failures);
                }
            }
            Assert.That(compared).Is.GreaterThan(0);
            FglState.AssertNone(failures, "XML asset lists differing from vanilla");
        }

        private static void Compare(ModContentPack mod, string folder, LoadableXmlAsset[] fgl, LoadableXmlAsset[] vanilla, List<string> failures)
        {
            string where = $"{mod.PackageIdPlayerFacing}/{folder}";
            if (fgl.Length != vanilla.Length)
            {
                failures.Add($"{where}: {fgl.Length} assets, vanilla {vanilla.Length}");
                return;
            }
            for (int i = 0; i < vanilla.Length; i++)
            {
                var a = fgl[i];
                var b = vanilla[i];
                // 順序本身就是載入資料夾的覆蓋結果，位置不同即視為不一致。
                if (!string.Equals(a.FullFilePath, b.FullFilePath, StringComparison.Ordinal))
                {
                    failures.Add($"{where}[{i}]: {a.FullFilePath}, vanilla {b.FullFilePath}");
                    return;
                }
                if (a.mod != b.mod)
                {
                    failures.Add($"{where}[{i}] {b.name}: owning mod differs");
                }
                if ((a.xmlDoc == null) != (b.xmlDoc == null)
                    || (a.xmlDoc != null && !string.Equals(a.xmlDoc.OuterXml, b.xmlDoc.OuterXml, StringComparison.Ordinal)))
                {
                    failures.Add($"{where}[{i}] {b.name}: parsed XML differs");
                }
            }
        }
    }

    /// <summary>未套用任何 patch 的原版 XmlAssetsInModFolder 副本。</summary>
    [HarmonyPatch]
    internal static class VanillaXml
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(DirectXmlLoader), nameof(DirectXmlLoader.XmlAssetsInModFolder))]
        public static LoadableXmlAsset[] XmlAssetsInModFolder(ModContentPack mod, string folderPath, List<string> foldersToLoadDebug)
            => throw new NotImplementedException("Replaced by Harmony reverse patch");
    }
}
