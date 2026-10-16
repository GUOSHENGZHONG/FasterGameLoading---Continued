using NUnit.Framework;
using UnityEngine;
using Waylith.Graphics;

namespace FasterGameLoading.Tests.StaticAtlasOptimizations;
[TestFixture]
public class StaticTextureAtlas_CalcRectsForAtlasNew_PatchTests
{
    [TestCase(194, 1)]
    [TestCase(306, 2)]
    public void TrimmedHeight_EveryMipLevelHeightIsBlockAligned(int usedRows, int mipCount)
    {
        int height = StaticTextureAtlas_CalcRectsForAtlasNew_Patch.TrimmedHeight(usedRows, mipCount);
        for (int mip = 0; mip < mipCount; mip++)
        {
            int mipHeight = height >> mip;
            // 區塊壓縮（BC／DXT）以 4 × 4 為單位；每層 mipmap 的高度都必須是 4 的倍數，這裡取更保守的 8。
            Assert.That(mipHeight % 8, Is.EqualTo(0), $"mip {mip} must be fully covered by block compression");
        }
    }

    /// <summary>
    /// 16384 寬的圖集有 6 層 mipmap：每層保留一列空白是原尺寸 32 列，高度以 256 列為單位。
    /// </summary>
    [TestCase(4300, 6, 4352)]
    [TestCase(100, 1, 104)]
    [TestCase(123, 1, 128)]
    [TestCase(124, 1, 128)]
    [TestCase(0, 3, 32)]
    [TestCase(100, 0, 104)]
    public void TrimmedHeight_KeepsOneEmptyRowPerMipLevelInBlockAlignedSteps(int usedRows, int mipCount, int expected)
    {
        Assert.That(StaticTextureAtlas_CalcRectsForAtlasNew_Patch.TrimmedHeight(usedRows, mipCount), Is.EqualTo(expected));
    }

    [Test]
    public void TrimmedHeight_EveryMipLevelIsAMultipleOfEight(
        [Values(1, 2, 3, 4, 5, 6, 7)] int mipCount,
        [Values(1, 57, 1000, 4301, 8191)] int usedRows)
    {
        int height = StaticTextureAtlas_CalcRectsForAtlasNew_Patch.TrimmedHeight(usedRows, mipCount);
        int lastMip = mipCount - 1;

        Assert.That((height >> lastMip) % 8, Is.EqualTo(0), "the smallest mip level must be block-aligned");
        Assert.That(height - usedRows, Is.GreaterThanOrEqualTo(1 << lastMip), "one empty row per mip level stays above the top texture");
        Assert.That(height - usedRows - (1 << lastMip), Is.LessThan(8 << lastMip), "no more than one rounding step");
    }

    [Test]
    public void CopyPageSettings_CarriesSamplingSettingsOver()
    {
        var from = new Texture2D(16, 16)
        {
            name = "TextureAtlas_Working",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 8,
            mipMapBias = -0.5f,
            minimumMipmapLevel = 0,
        };
        var to = new Texture2D(16, 8);
        from.CoreTexture.MemoryCategory = GpuMemoryCategory.Atlas;

        StaticTextureAtlas_CalcRectsForAtlasNew_Patch.CopyPageSettings(from, to);

        Assert.Multiple(() =>
        {
            Assert.That(to.name, Is.EqualTo("TextureAtlas_Working"));
            Assert.That(to.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(to.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(to.anisoLevel, Is.EqualTo(8));
            Assert.That(to.mipMapBias, Is.EqualTo(-0.5f));
            Assert.That(to.minimumMipmapLevel, Is.EqualTo(0));
            Assert.That(to.CoreTexture.MemoryCategory, Is.EqualTo(GpuMemoryCategory.Atlas));
        });
    }

    [Test]
    public void RescaleRows_KeepsEveryTextureOnTheSamePixelRows()
    {
        var rect = new Rect(0.25f, 1000f / 8192f, 0.125f, 64f / 8192f);

        var rescaled = StaticTextureAtlas_CalcRectsForAtlasNew_Patch.RescaleRows(rect, 8192, 4352);

        Assert.That(rescaled.x, Is.EqualTo(rect.x));
        Assert.That(rescaled.width, Is.EqualTo(rect.width));
        Assert.That(rescaled.y * 4352f, Is.EqualTo(1000f).Within(0.001f));
        Assert.That(rescaled.height * 4352f, Is.EqualTo(64f).Within(0.001f));
    }
}
