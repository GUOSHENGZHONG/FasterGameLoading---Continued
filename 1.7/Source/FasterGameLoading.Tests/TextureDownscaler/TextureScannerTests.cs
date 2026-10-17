using NUnit.Framework;

namespace FasterGameLoading.Tests.TextureDownscaler;

[TestFixture]
public class TextureScannerTests
{
    [Test]
    public void TextureScanner_Instantiable()
    {
        var scanner = new TextureScanner();
        Assert.That(scanner, Is.Not.Null);
    }
}
