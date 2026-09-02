namespace Quartz.Tests.Unit;

/// <summary>
/// The base exception type of the whole library, and the two things its own documentation had wrong
/// about it.
/// </summary>
public class SchedulerExceptionTest
{
    /// <summary>
    /// The cause's message becomes the exception's, in the base constructor call — so a null cause was
    /// a <see cref="NullReferenceException" /> raised while constructing the exception that was meant to
    /// report something else.
    /// </summary>
    [Test]
    public void AnExceptionWrappingNothingSaysWhichArgumentIsMissing()
    {
        Action construct = () => throw new SchedulerException((Exception) null!);

        construct.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("innerException",
                "the parameter is non-nullable, so this is only ever a caller's bug — and it should read "
                + "like one rather than like a fault inside Quartz");
    }

    [Test]
    public void AnExceptionWrappingACauseTakesItsMessageAndAppendsItToToString()
    {
        InvalidOperationException cause = new("the database went away");
        SchedulerException failure = new(cause);

        failure.Message.Should().Be("the database went away");
        failure.InnerException.Should().BeSameAs(cause);
        failure.ToString().Should().Contain("See nested exception:",
            "ToString is overridden precisely so the cause is visible in a log line");
    }

    [Test]
    public void AnExceptionWithNoCauseFormatsAsAnOrdinaryException()
    {
        SchedulerException failure = new("nothing wrapped");

        failure.ToString().Should().NotContain("See nested exception:");
    }
}
