using HarmonyLib;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 較快的靜態圖集壓縮（預設關閉）。
    /// 原版在 CPU 路徑以 Texture2D.Compress(highQuality: true) 壓縮圖集；高品質模式會加上抖色處理，耗時較長。
    /// 開啟後改用 Compress(highQuality: false)，其餘與原版的 ApplyTextureCompression 相同，GPU 路徑（FastCompressDXT）不受影響。
    /// 這裡呼叫原生的 Compress，不對它打補丁：Harmony 對 InternalCall 打補丁不會報錯，但被打補丁的方法會變成什麼都不做。
    /// </summary>
    [HarmonyPatch(typeof(StaticTextureAtlas), nameof(StaticTextureAtlas.ApplyTextureCompression))]
    public static class StaticTextureAtlas_ApplyTextureCompression_Patch
    {
        public static bool Prefix(StaticTextureAtlas __instance, bool noGpuCompressionSupport)
        {
            if (!noGpuCompressionSupport || !FasterGameLoadingSettings.FastStaticAtlasCompression)
            {
                return true;
            }
            DeepProfiler.Start("Compress atlas textures");
            try
            {
                if (__instance.colorTexture != null)
                {
                    __instance.colorTexture.Compress(highQuality: false);
                }
                if (__instance.maskTexture != null)
                {
                    __instance.maskTexture.Compress(highQuality: false);
                }
            }
            finally
            {
                DeepProfiler.End();
            }
            return false;
        }
    }
}
