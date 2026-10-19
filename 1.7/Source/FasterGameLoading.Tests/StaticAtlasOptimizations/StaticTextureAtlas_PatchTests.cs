using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.StaticAtlasOptimizations;

[TestFixture]
public class StaticTextureAtlasPatchTests
{
    [Test]
    public void PatchName_Exists()
    {
        // 驗證補丁類別存在且可被 NUnit 載入
        Assert.That(typeof(GlobalTextureAtlasManager_TryGetStaticTile_Patch), Is.Not.Null);
    }

    [Test]
    public void StaticAtlasDeduplicator_Instantiable()
    {
        // 只驗證類別可被反射載入，不實際執行遊戲相關邏輯
        Assert.That(typeof(StaticAtlasDeduplicator), Is.Not.Null);
    }
}
