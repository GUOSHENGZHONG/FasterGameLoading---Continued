using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimTestRedux;
using UnityEngine;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 延遲的圖示或貼圖若在 UI 需要時還沒就緒（或重載後仍指向已銷毀的舊貼圖），
    /// Unity 只會每幀記一行「null texture passed to GUI.DrawTexture」，不會丟例外。
    /// 以探針記下每一輪呼叫端的堆疊，讓來源一目了然。
    /// </summary>
    [TestSuite]
    internal static class NullTextureDrawTests
    {
        [Test]
        public static void NoNullTextureIsDrawn()
        {
            List<string> failures;
            lock (NullTextureDrawProbe.Callers)
            {
                failures = NullTextureDrawProbe.Callers
                    .Where(static e => e.Key.round == TestRunDriver.Round)
                    .Select(static e => $"{e.Value}x from {e.Key.caller}")
                    .ToList();
            }
            FglState.AssertNone(failures, "call sites drawing null textures", maxListed: 5);
        }
    }

    [HarmonyPatch]
    internal static class NullTextureDrawProbe
    {
        /// <summary>(輪次, 呼叫端堆疊摘要) → 次數。</summary>
        public static readonly Dictionary<(int round, string caller), int> Callers = new Dictionary<(int, string), int>();

        /// <summary>
        /// 以參數型別篩選：DrawTexture 的第 2 個參數是貼圖。找不到任何多載（Unity 改了簽章）時以 Prepare 停用探針，
        /// 否則 TargetMethods 回傳空集合會讓 Harmony 丟例外，整個測試 mod 的 PatchAll 跟著失敗。
        /// </summary>
        private static readonly List<MethodBase> Targets = typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static m => m.Name == nameof(GUI.DrawTexture) && m.GetParameters() is { Length: >= 2 } p && p[1].ParameterType == typeof(Texture))
            .Cast<MethodBase>()
            .ToList();

        public static bool Prepare() => Targets.Count > 0;

        public static IEnumerable<MethodBase> TargetMethods() => Targets;

        private static readonly Assembly RimTestReduxAssembly = typeof(RimTestReduxMod).Assembly;

        public static void Prefix([HarmonyArgument(1)] Texture image)
        {
            if (image != null) return;
            // 多載彼此轉呼叫（被 patch 後是名為 ...DrawTexture_Patch 的動態方法），同一次繪製會經過數層；略過這些，取實際呼叫端。
            var frames = new StackTrace(1).GetFrames()
                .Select(static f => f.GetMethod())
                .Where(static m => m != null && !m.Name.Contains(nameof(GUI.DrawTexture)))
                .ToList();
            // RimTest Redux 把工具列圖示存在 [StaticConstructorOnStartup] 的靜態欄位，語言重載後指向已銷毀的貼圖；
            // 這是測試框架自己的限制，與 FGL 無關。
            if (frames.Any(static m => m.DeclaringType?.Assembly == RimTestReduxAssembly)) return;
            var caller = string.Join(" <- ", frames.Take(4).Select(static m => $"{m.DeclaringType?.Name}.{m.Name}"));
            lock (Callers)
            {
                var key = (TestRunDriver.Round, caller);
                Callers.TryGetValue(key, out int count);
                Callers[key] = count + 1;
            }
        }
    }
}
