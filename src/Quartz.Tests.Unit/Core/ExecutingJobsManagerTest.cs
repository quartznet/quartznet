using NUnit.Framework;
using Quartz.Core;

namespace Quartz.Tests.Unit.Core
{
    [TestFixture]
    public class ExecutingJobsManagerTest
    {
        private readonly ExecutingJobsManager _executingJobsManager;

        public ExecutingJobsManagerTest()
        {
            _executingJobsManager = new ExecutingJobsManager();
        }

        [Test]
        public void Name()
        {
            var actual = _executingJobsManager.Name;
            Assert.AreEqual("Quartz.Core.ExecutingJobsManager", actual);
            Assert.AreSame(actual, _executingJobsManager.Name);
        }
    }
}
