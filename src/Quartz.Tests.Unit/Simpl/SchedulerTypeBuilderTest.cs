using FluentAssertions;

using Quartz;
using Quartz.Impl;
using Quartz.Simpl;

using SchedulerTypeBuilderTestTypes;

namespace Quartz.Tests.Unit.Simpl
{
    public class SchedulerTypeBuilderTest
    {
        [Test]
        public void ShouldBeAbleToCreateTypeForInterface()
        {
            RunTest<IMyScheduler>();

            // Should be able to create same type multiple times
            RunTest(typeof(IMyScheduler));
        }

        [Test]
        public void ShouldBeAbleToCreateTypeForInterfaceWithoutNameSpace()
        {
            RunTest<IMySchedulerWithoutNameSpace>();
        }

        [Test]
        public void ShouldBeAbleToCreateTypeForInterfacesWithSameNameInDifferentNamespaces()
        {
            RunTest<IMyScheduler>();
            RunTest<SchedulerTypeBuilderTestTypesB.IMyScheduler>();
        }

        [Test]
        public void ShouldValidateGivenInterface()
        {
            AssertThrows(typeof(DummyClass));
            AssertThrows(typeof(INonPublicInterface));
            AssertThrows(typeof(IGenericInterface<int>));
            AssertThrows(typeof(IDummyInterface));
            AssertThrows(typeof(INestedInterface));
            AssertThrows(typeof(IInterfaceWhichImplementMultipleInterfaces));

            static void AssertThrows(Type type)
            {
                Assert.Throws<ArgumentException>(() => SchedulerTypeBuilder.Create(type))!.ParamName.Should().Be("interfaceType");
            }
        }

        private static void RunTest<TScheduler>() where TScheduler : class, IScheduler
        {
            var cratedType = SchedulerTypeBuilder.Create<TScheduler>();
            AssertCreatedType(cratedType, typeof(TScheduler));
        }

        private static void RunTest(Type type)
        {
            var cratedType = SchedulerTypeBuilder.Create(type);
            AssertCreatedType(cratedType, type);
        }

        private static void AssertCreatedType(Type schedulerType, Type interfaceType)
        {
            schedulerType.Should().NotBeNull();

            var dummyScheduler = new DelegatingScheduler(null!);
            var scheduler = Activator.CreateInstance(schedulerType, dummyScheduler);

            scheduler.Should().NotBeNull();
            scheduler.Should().BeAssignableTo<IScheduler>();
            scheduler.Should().BeAssignableTo(interfaceType);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public interface INestedInterface : IScheduler;
    }
}

namespace SchedulerTypeBuilderTestTypes
{
    public interface IMyScheduler : IScheduler;

    public class DummyClass;

    public interface IDummyInterface;

    public interface IGenericInterface<T> : IScheduler;

    public interface IInterfaceWhichImplementMultipleInterfaces : IScheduler, ICalendar;

    internal interface INonPublicInterface : IScheduler;
}

namespace SchedulerTypeBuilderTestTypesB
{
    public interface IMyScheduler : IScheduler;
}

public interface IMySchedulerWithoutNameSpace : IScheduler;