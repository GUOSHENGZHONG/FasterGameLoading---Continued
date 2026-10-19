using NUnit.Framework;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking;

[TestFixture]
public class AtlasBakeDiagnosticsTests
{
    [Test]
    public void LogPotentialMaskIssues_DoesNotThrow()
    {
        // 診斷工具應在任何情況下（包含空 buildQueue）都不丟出例外
        Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-context"));
    }

    [Test]
    public void LogPotentialMaskIssues_WithEmptyString()
    {
        Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues(""));
    }
}
