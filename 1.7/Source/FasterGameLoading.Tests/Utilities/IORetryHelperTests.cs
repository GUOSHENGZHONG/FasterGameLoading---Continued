using System;
using System.IO;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Utilities
{
    [TestFixture]
    public class IORetryHelperTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "FGL_IORetry_" + Guid.NewGuid().ToString("N"));
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
                Assert.Fail("清理 I/O 測試暫存目錄失敗：" + tempDir + Environment.NewLine + ex);
            }
        }

        [Test]
        public void WriteAllBytesWithRetry_CreatesAtomicTargetAndRemovesTemporaryFile()
        {
            var path = Path.Combine(tempDir, "cache.bin");
            var bytes = new byte[] { 0, 1, 2, 255 };

            IORetryHelper.WriteAllBytesWithRetry(path, bytes, maxRetries: 1, delayMs: 0);

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [Test]
        public void WriteAllBytesWithRetry_ReplacesExistingTarget()
        {
            var path = Path.Combine(tempDir, "cache.bin");
            File.WriteAllBytes(path, new byte[] { 1 });

            IORetryHelper.WriteAllBytesWithRetry(path, new byte[] { 2, 3 }, maxRetries: 1, delayMs: 0);

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 2, 3 }));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [Test]
        public void WriteAllBytesWithRetry_WhenDestinationDirectoryDoesNotExist_ThrowsAndCleansUpTemp()
        {
            var invalidPath = Path.Combine(tempDir, "non_existent_folder", "file.bin");

            Assert.Throws<DirectoryNotFoundException>(() =>
            {
                IORetryHelper.WriteAllBytesWithRetry(invalidPath, new byte[] { 1, 2, 3 }, maxRetries: 1, delayMs: 0);
            });

            Assert.That(File.Exists(invalidPath + ".tmp"), Is.False);
        }
    }
}
