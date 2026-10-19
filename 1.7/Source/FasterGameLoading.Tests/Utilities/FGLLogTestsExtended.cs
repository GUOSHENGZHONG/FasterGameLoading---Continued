using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Utilities;

[TestFixture]
public class FGLLogTestsExtended
{
    [Test]
    public void FGLLog_Message_DoesNotThrow_WhenVerboseDisabled()
    {
        // 當 VerboseLogging 為 false 時，Message 不會執行任何寫入動作
        Assert.DoesNotThrow(() => FGLLog.Message("test-message"));
    }

    [Test]
    public void FGLLog_Warning_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => FGLLog.Warning("test-warning"));
    }

    [Test]
    public void FGLLog_Error_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => FGLLog.Error("test-error"));
    }

    [Test]
    public void FGLLog_Error_WithException_DoesNotThrow()
    {
        var ex = new System.Exception("test exception");
        Assert.DoesNotThrow(() => FGLLog.Error("test-error", ex));
    }
}
