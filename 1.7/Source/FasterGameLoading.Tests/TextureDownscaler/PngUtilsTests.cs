using System.IO;
using NUnit.Framework;

namespace FasterGameLoading.Tests.TextureDownscaler;

[TestFixture]
public class PngUtilsTests
{
    [Test]
    public void TryGetImageDimensions_NonExistentFile_ReturnsFalse()
    {
        int w = 0, h = 0;
        var result = PngUtils.TryGetImageDimensions("C:\\NotARealFile.png", ref w, ref h);
        Assert.That(result, Is.False);
        Assert.That(w, Is.EqualTo(0));
        Assert.That(h, Is.EqualTo(0));
    }

    [Test]
    public void TryReadPngDimensions_InvalidHeader_ReturnsFalse()
    {
        // 使用一個無效 PNG 標頭的暫存檔案驗證解析拒絕
        var path = Path.Combine(Path.GetTempPath(), "bad_png_" + System.Guid.NewGuid() + ".png");
        File.WriteAllText(path, "NOT A PNG");
        try
        {
            int w = 0, h = 0;
            Assert.That(PngUtils.TryGetImageDimensions(path, ref w, ref h), Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
