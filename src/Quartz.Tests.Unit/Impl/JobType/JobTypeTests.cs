using System;

using FluentAssertions;

using NUnit.Framework;

namespace Quartz.Tests.Unit.Impl.JobType;

public class JobTypeTests
{
    [Test]
    public void JobTypeMustImplementIJob()
    {
        Action act = () => new Quartz.Impl.JobType(typeof(ClassDoesNotImplementIJobType));

        act.Should().Throw<ArgumentException>()
            .WithMessage("Job class must implement Quartz.IJob interface.");
    }

    public class ClassDoesNotImplementIJobType
    {
    }
}