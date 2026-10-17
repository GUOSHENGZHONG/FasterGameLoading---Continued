using NUnit.Framework;

namespace FasterGameLoading.Tests.StaticAtlasOptimizations;

[TestFixture]
public class GlobalTextureAtlasManagerPatchTests
{
    [Test]
    public void GlobalTextureAtlasManager_BakeStaticAtlases_DeduplicatePatch_Exists()
    {
        // 確認補丁類別可被載入
        Assert.That(typeof(GlobalTextureAtlasManager_BakeStaticAtlases_DeduplicatePatch), Is.Not.Null);
    }

    [Test]
    public void GlobalTextureAtlasManager_TryGetStaticTile_Patch_Exists()
    {
        Assert.That(typeof(GlobalTextureAtlasManager_TryGetStaticTile_Patch), Is.Not.Null);
    }
}
