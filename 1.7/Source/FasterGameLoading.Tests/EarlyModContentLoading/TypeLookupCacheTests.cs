using NUnit.Framework;

namespace FasterGameLoading.Tests.EarlyModContentLoading;

[TestFixture]
public class TypeLookupCacheTests
{
    [Test]
    public void FullNamesFromLastSession_Exists()
    {
        Assert.That(FasterGameLoading.TypeLookupCache.FullNamesFromLastSession, Is.Not.Null);
    }

    [Test]
    public void SessionMappingSettled_DefaultFalse()
    {
        // 新 session 啟動時未結算
        Assert.That(FasterGameLoading.TypeLookupCache.SessionMappingSettled, Is.False);
    }

    [Test]
    public void ComputeAssemblyFingerprint_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => FasterGameLoading.TypeLookupCache.ComputeCurrentFingerprint());
    }
}
