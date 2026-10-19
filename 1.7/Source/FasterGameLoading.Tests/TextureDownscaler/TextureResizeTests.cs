using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.TextureDownscaler;

[TestFixture]
public class TextureResizeTests
{
    [Test]
    public void TextureType_EnumValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(TextureResize.TextureType), TextureResize.TextureType.Building), Is.True);
        Assert.That(Enum.IsDefined(typeof(TextureResize.TextureType), TextureResize.TextureType.Pawn), Is.True);
        Assert.That(Enum.IsDefined(typeof(TextureResize.TextureType), TextureResize.TextureType.Weapon), Is.True);
    }

    [Test]
    public void TextureResizeCandidate_FieldsAccessible()
    {
        var candidate = new TextureResize.TextureResizeCandidate();
        // 驗證結構體欄位可存取（編譯層面）
        Assert.That(candidate, Is.Not.Null);
    }
}
