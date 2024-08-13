using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

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
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo("Quartz.Core.ExecutingJobsManager"));
            Assert.That(_executingJobsManager.Name, Is.SameAs(actual));
        });
    }
}