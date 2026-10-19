using NUnit.Framework;

namespace FasterGameLoading.Tests.Settings;

[TestFixture]
public class FasterGameLoadingSettingsExtendedTests
{
    [Test]
    public void DefaultValues_AreSet()
    {
        // 驗證預設值符合預期（不實際修改，僅讀取確認存在）
        Assert.That(FasterGameLoadingSettings.TrimStaticAtlases, Is.True);
        Assert.That(FasterGameLoadingSettings.DeduplicateStaticAtlases, Is.True);
        Assert.That(FasterGameLoadingSettings.EnableMultiThreading, Is.True);
        Assert.That(FasterGameLoadingSettings.TypeLookupCache, Is.True);
    }

    [Test]
    public void EarlyModContentLoading_FieldExists()
    {
        // 確認為 loading-progress 整合而保留的公開欄位仍存在
        Assert.That(FasterGameLoadingSettings.earlyModContentLoading, Is.True);
    }
}
