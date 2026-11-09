using FloorF.UI;
using NUnit.Framework;

namespace FloorF.Tests.PlayMode
{
    /// <summary>
    /// For every PlayMode test (all modules): the flow-changing UI features (title attract screen, establishing shots, rule
    /// box auto-collapse) are off, so the title shows its menu at once and every Intro shows its card at once - as the
    /// tests were written. A test about one of them switches it on (UICinematicsTests); UITestDriver.Reset switches it off.
    /// </summary>
    [SetUpFixture]
    public class UIFeatureDefaultsForTests
    {
        [OneTimeSetUp]
        public void FeaturesOff() => UIFeatures.SetAll(false);

        [OneTimeTearDown]
        public void FeaturesOn() => UIFeatures.SetAll(true);
    }
}
