using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTestRedux;
using RimWorld;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 除了 GenTypes.GetTypeInAnyAssemblyInt，FGL 還快取了 AccessTools.AllTypes、AccessTools.TypeByName
    /// 與 GenTypes.AllLeafSubclasses。前兩者以 reverse patch 的原版副本、後者依原版定義重算，在真實的組件組合上比對。
    /// </summary>
    [TestSuite]
    internal static class ReflectionCacheTests
    {
        /// <summary>常被 mod 用來找延伸點的基底型別，另加上快取中已經算過的所有基底型別。</summary>
        private static readonly Type[] LeafBaseTypes =
        {
            typeof(Def), typeof(Thing), typeof(ThingComp), typeof(CompProperties), typeof(HediffComp),
            typeof(Designator), typeof(Window), typeof(Mod), typeof(Verb), typeof(GameComponent),
            typeof(MapComponent), typeof(RimWorld.Planet.WorldComponent), typeof(PatchOperation), typeof(StatPart),
        };

        /// <summary>
        /// FGL 以組件數判斷快取是否過期；組件載入後數量沒變就會沿用舊清單。
        /// 呼叫端常以 FirstOrDefault 取第一個同名型別，順序也必須相同。
        /// </summary>
        [Test]
        public static void AllTypesMatchesVanillaInOrder()
        {
            var fgl = AccessTools.AllTypes().ToList();
            var vanilla = VanillaReflection.AllTypes().ToList();
            Assert.That(fgl.Count).Is.EqualTo(vanilla.Count);
            for (int i = 0; i < vanilla.Count; i++)
            {
                if (fgl[i] != vanilla[i])
                {
                    throw new AssertionException($"AllTypes differs at index {i}: {fgl[i]?.FullName}, vanilla {vanilla[i]?.FullName}");
                }
            }
        }

        /// <summary>快取中每個名稱都必須解析到原版會解析到的型別。</summary>
        [Test]
        public static void CachedTypeByNameMatchesVanilla()
        {
            if (!FasterGameLoadingSettings.TypeLookupCache) return;

            // 隔離的測試 session 可能沒有任何 mod 呼叫 TypeByName；先以全名、短名稱各查兩次，第二次必定命中快取。
            foreach (var name in new[] { "Verse.ThingDef", "CompProperties_Glower", "HarmonyLib.AccessTools" })
            {
                AccessTools.TypeByName(name);
                AccessTools.TypeByName(name);
            }

            var failures = new List<string>();
            foreach (var entry in AccessTools_TypeByName_Patch.cachedResults.ToArray())
            {
                var vanilla = VanillaReflection.TypeByName(entry.Key);
                if (vanilla != entry.Value)
                {
                    failures.Add($"{entry.Key}: cached {entry.Value?.FullName}, vanilla {vanilla?.FullName ?? "null"}");
                }
            }
            Assert.That(AccessTools_TypeByName_Patch.cachedResults.Count).Is.GreaterThan(0);
            FglState.AssertNone(failures, "cached TypeByName results differing from vanilla");
        }

        /// <summary>查無結果不能被快取，否則之後才定義該型別的組件永遠查不到。</summary>
        [Test]
        public static void MissingTypeByNameIsNotCached()
        {
            if (!FasterGameLoadingSettings.TypeLookupCache) return;

            const string missing = "FasterGameLoading.InGameTests.NoSuchType";
            Assert.That(AccessTools.TypeByName(missing) == null).Is.True();
            Assert.That(AccessTools_TypeByName_Patch.cachedResults.ContainsKey(missing)).Is.False();
        }

        /// <summary>原版 AllSubclasses 以 PLINQ 產生，順序不固定，因此以集合比對。</summary>
        [Test]
        public static void AllLeafSubclassesMatchesVanilla()
        {
            var baseTypes = new HashSet<Type>(LeafBaseTypes);
            baseTypes.UnionWith(GenTypes_AllLeafSubclasses_Patch.keyValuePairs.Keys);

            var failures = new List<string>();
            foreach (var baseType in baseTypes)
            {
                var fgl = new HashSet<Type>(baseType.AllLeafSubclasses());
                var vanilla = VanillaLeafSubclasses(baseType);
                if (fgl.SetEquals(vanilla)) continue;

                var extra = fgl.Except(vanilla).Select(static t => t.FullName).Take(3);
                var missing = vanilla.Except(fgl).Select(static t => t.FullName).Take(3);
                failures.Add($"{baseType.Name}: extra [{string.Join(", ", extra)}], missing [{string.Join(", ", missing)}]");
            }
            FglState.AssertNone(failures, "AllLeafSubclasses results differing from vanilla");
        }

        /// <summary>
        /// 原版定義：AllSubclasses 中「自己的 AllSubclasses 為空」的型別。直接呼叫原版會對每個子類別各掃一次全部型別，
        /// 這裡改用同一份 AllSubclasses：型別 t 有子類別 ⇔ 某個 u 的 BaseType 鏈（即 IsSubclassOf 走訪的鏈）經過 t。
        /// </summary>
        private static HashSet<Type> VanillaLeafSubclasses(Type baseType)
        {
            var subclasses = baseType.AllSubclasses();
            var hasSubclass = new HashSet<Type>();
            foreach (var type in subclasses)
            {
                for (var ancestor = type.BaseType; ancestor != null && ancestor != baseType; ancestor = ancestor.BaseType)
                {
                    hasSubclass.Add(ancestor);
                }
            }
            return new HashSet<Type>(subclasses.Where(t => !hasSubclass.Contains(t)));
        }
    }

    /// <summary>未套用任何 patch 的原版反射查詢副本。</summary>
    [HarmonyPatch]
    internal static class VanillaReflection
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(AccessTools), nameof(AccessTools.AllTypes))]
        public static IEnumerable<Type> AllTypes()
            => throw new NotImplementedException("Replaced by Harmony reverse patch");

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(AccessTools), nameof(AccessTools.TypeByName), typeof(string))]
        public static Type TypeByName(string name)
            => throw new NotImplementedException("Replaced by Harmony reverse patch");
    }
}
