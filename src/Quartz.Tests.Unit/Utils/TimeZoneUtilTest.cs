using NUnit.Framework;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils
{
    [TestFixture]
    public class TimeZoneUtilTest
    {
        [Test]
        public void ShouldBeAbleToFindWithAlias()
        {
            var infoWithUtc = TimeZoneUtil.FindTimeZoneById("UTC");
            var infoWithUniversalCoordinatedTime = TimeZoneUtil.FindTimeZoneById("Coordinated Universal Time");

            Assert.AreEqual(infoWithUtc, infoWithUniversalCoordinatedTime);
        }
    }
}