using System;
using DevTools.Benchmarking;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace FasterGameLoading.InGameTests
{
    // 比較遊戲內重複型別查詢的成本；完整啟動時間仍需獨立量測。
    [BenchmarkClass("FGL type lookup", RunAsync = false, AllowedGameStates = AllowedGameStates.Entry)]
    [Measurement(Benchmark.Measurement.Microseconds)]
    [Table(Stat.Mean | Stat.Median | Stat.StdDev | Stat.Samples)]
    internal static class FglBenchmarks
    {
        private const string TypeName = "Verse.ThingDef";

        [Benchmark(Label = "AccessTools.TypeByName")]
        private static Type CachedTypeByName() => AccessTools.TypeByName(TypeName);

        [Benchmark(Label = "Unpatched AccessTools.TypeByName")]
        private static Type VanillaTypeByName() => VanillaReflection.TypeByName(TypeName);

        [Benchmark(Label = "GenTypes full name with namespace")]
        private static Type CachedGenTypes() => GenTypes.GetTypeInAnyAssemblyInt(TypeName, "Verse");

        [Benchmark(Label = "Unpatched GenTypes full name with namespace")]
        private static Type VanillaGenTypes() => VanillaLookup.GetTypeInAnyAssemblyInt(TypeName, "Verse");
    }
}
