using System;
using System.IO;
using NUnit.Framework;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class PngUtilsTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "FGL_PngUtils_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                Assert.Fail("清理 PNG 測試暫存目錄失敗：" + tempDir + Environment.NewLine + ex);
            }
        }

        [Test]
        public void TryGetImageDimensions_WithValidPngSignature_ReadsBigEndianDimensions()
        {
            var path = Path.Combine(tempDir, "valid.png");
            File.WriteAllBytes(path, TestFixtures.CreatePngHeader(4096, 2048));
            var width = 0;
            var height = 0;

            var result = PngUtils.TryGetImageDimensions(path, ref width, ref height);

            Assert.That(result, Is.True);
            Assert.That(width, Is.EqualTo(4096));
            Assert.That(height, Is.EqualTo(2048));
        }

        [TestCase(10, 100)]
        [TestCase(0, 100)]
        [TestCase(16385, 100)]
        public void TryGetImageDimensions_WithInvalidDimensionsOrShortHeader_ReturnsFalse(
            int widthValue,
            int heightValue)
        {
            var path = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".png");
            var bytes = TestFixtures.CreatePngHeader(widthValue, heightValue);
            if (widthValue is 10)
            {
                Array.Resize(ref bytes, 10);
            }
            File.WriteAllBytes(path, bytes);
            var width = 7;
            var height = 9;

            var result = PngUtils.TryGetImageDimensions(path, ref width, ref height);

            Assert.That(result, Is.False);
            Assert.That(width, Is.EqualTo(7));
            Assert.That(height, Is.EqualTo(9));
        }

        [Test]
        public void TryGetImageDimensions_WithMissingFile_ReturnsFalse()
        {
            var width = 1;
            var height = 1;

            var result = PngUtils.TryGetImageDimensions(
                Path.Combine(tempDir, "missing.png"), ref width, ref height);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryGetImageDimensions_WithWrongSignature_ReturnsFalseWithoutThrowing()
        {
            var path = Path.Combine(tempDir, "not-png.bin");
            var bytes = TestFixtures.CreatePngHeader(10, 20);
            bytes[0] = 0;
            File.WriteAllBytes(path, bytes);
            var width = 7;
            var height = 9;

            Assert.That(PngUtils.TryGetImageDimensions(path, ref width, ref height), Is.False);
            Assert.That(width, Is.EqualTo(7));
            Assert.That(height, Is.EqualTo(9));
        }
    }
}
