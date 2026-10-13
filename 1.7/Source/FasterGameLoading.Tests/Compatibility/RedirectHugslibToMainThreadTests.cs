using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class RedirectHugslibToMainThreadTests
    {
        [Test]
        public void Prepare_IsDisabledWhenDelayedGraphicLoadingIsDisabled()
        {
            var previous = FasterGameLoadingSettings.DelayGraphicLoading;
            try
            {
                FasterGameLoadingSettings.DelayGraphicLoading = false;

                Assert.That(RedirectHugslibToMainThread.Prepare(), Is.False);
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = previous;
            }
        }

        [Test]
        public void Prepare_WhenDelayGraphicLoadingIsTrue_MatchesTargetMethodPresence()
        {
            var previous = FasterGameLoadingSettings.DelayGraphicLoading;
            try
            {
                FasterGameLoadingSettings.DelayGraphicLoading = true;

                Assert.That(
                    RedirectHugslibToMainThread.Prepare(),
                    Is.EqualTo(RedirectHugslibToMainThread.targetMethod != null));
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = previous;
            }
        }

        [Test]
        public void TargetMethod_ReturnsTheCachedOptionalHugsLibMethod()
        {
            Assert.That(
                RedirectHugslibToMainThread.TargetMethod(),
                Is.SameAs(RedirectHugslibToMainThread.targetMethod));
        }

        [Test]
        public void Prefix_ReturnsFalseAndQueuesAction()
        {
            var dummyInstance = new object();
            var result = RedirectHugslibToMainThread.Prefix(dummyInstance);

            Assert.That(result, Is.False);
        }

        [Test]
        public void OnDefsLoaded_ThrowsNotImplementedExceptionAsStub()
        {
            Assert.Throws<NotImplementedException>(() => RedirectHugslibToMainThread.OnDefsLoaded(new object()));
        }
    }
}
