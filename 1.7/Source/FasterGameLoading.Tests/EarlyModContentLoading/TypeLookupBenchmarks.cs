using System;
using System.Diagnostics;
using NUnit.Framework;

namespace FasterGameLoading.Tests.EarlyModContentLoading;

/// <summary>手動執行的局部基準；不以時間門檻判斷正確性，也不代表完整啟動時間。</summary>
[TestFixture, NonParallelizable]
public class TypeLookupBenchmarks
{
    [Test, Explicit("比較已列舉組件的全名預熱成本；不包含首次反射列舉")]
    public void WarmupFullNames()
    {
        var assemblies = new[] { typeof(Verse.ThingDef).Assembly, typeof(GenTypes_GetTypeInAnyAssemblyInt_Patch).Assembly, typeof(HarmonyLib.Harmony).Assembly };
        int count = 0;
        foreach (var assembly in assemblies) count += AssemblyTypesCache.Get(assembly).Length;
        try
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.WarmupFullNames(assemblies);
            for (int sample = 0; sample < 7; sample++)
            {
                GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                GenTypes_GetTypeInAnyAssemblyInt_Patch.WarmupFullNames(assemblies);
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                TestContext.Out.WriteLine($"sample={sample}, types={count}, ms={ms:F3}, bytes={allocated}");
            }
        }
        finally
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
        }
    }

    [Test, Explicit("以 Release 執行並比較相同環境下的中位數與配置量")]
    public void FullNameWithNamespaceHit()
    {
        const int iterations = 200_000;
        var names = new[] { typeof(Verse.ThingDef).FullName, typeof(Verse.Pawn).FullName, typeof(Verse.Def).FullName };
        var types = new[] { typeof(Verse.ThingDef), typeof(Verse.Pawn), typeof(Verse.Def) };
        GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
        try
        {
            for (int i = 0; i < names.Length; i++)
                GenTypes_GetTypeInAnyAssemblyInt_Patch.cachedResults[names[i]] = types[i];
            Run(20_000);
            for (int sample = 0; sample < 7; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Run(iterations);
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                TestContext.Out.WriteLine($"sample={sample}, calls={iterations}, ms={ms:F3}, bytes={allocated}");
            }

            void Run(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    string name = names[i % names.Length];
                    Type result = null;
                    bool original = GenTypes_GetTypeInAnyAssemblyInt_Patch.Prefix(ref result, out var state, ref name, "Verse");
                    GenTypes_GetTypeInAnyAssemblyInt_Patch.Postfix(ref result, state);
                    if (original || result != types[i % types.Length]) throw new InvalidOperationException("快取命中結果不符。");
                }
            }
        }
        finally
        {
            GenTypes_GetTypeInAnyAssemblyInt_Patch.ClearCache();
        }
    }
}
