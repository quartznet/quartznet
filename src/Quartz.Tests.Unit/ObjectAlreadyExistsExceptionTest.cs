using Quartz.Impl.Triggers;
using Quartz.Jobs;

namespace Quartz.Tests.Unit;

/// <summary>
/// The exception carries the identity that clashed, so that a caller handling it does not have to
/// parse the key back out of the message.
/// </summary>
public class ObjectAlreadyExistsExceptionTest
{
    [Test]
    public void TheJobConstructorCarriesTheJobsKey()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("import", "nightly").Build();

        ObjectAlreadyExistsException exception = new ObjectAlreadyExistsException(job);

        exception.JobKey.Should().Be(new JobKey("import", "nightly"));
        exception.TriggerKey.Should().BeNull("a job's clash is not a trigger's");
        exception.Message.Should().Contain("nightly.import", "the message is unchanged");
    }

    [Test]
    public void TheTriggerConstructorCarriesTheTriggersKey()
    {
        ITrigger trigger = new SimpleTriggerImpl { Key = new TriggerKey("hourly", "imports") };

        ObjectAlreadyExistsException exception = new ObjectAlreadyExistsException(trigger);

        exception.TriggerKey.Should().Be(new TriggerKey("hourly", "imports"));
        exception.JobKey.Should().BeNull("a trigger's clash is not a job's");
        exception.Message.Should().Contain("imports.hourly", "the message is unchanged");
    }

    [Test]
    public void TheMessageOnlyConstructorNamesNothing()
    {
        ObjectAlreadyExistsException exception = new ObjectAlreadyExistsException("something already exists");

        exception.JobKey.Should().BeNull();
        exception.TriggerKey.Should().BeNull();
    }
}
