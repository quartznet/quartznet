using FakeItEasy;

namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// What a fake <see cref="IScheduler" /> needs to answer the way a scheduler written against 4.2 does.
/// </summary>
internal static class FakeSchedulers
{
    /// <summary>
    /// Makes a fake answer the <c>*With</c> pauses through the reasonless member each default interface
    /// member wraps.
    /// </summary>
    /// <remarks>
    /// A fake intercepts a default interface member like any other, and the pause routes call the
    /// <c>*With</c> members since 4.3. Without this, a case that arranges or asserts
    /// <see cref="IScheduler.PauseAll" /> would see the route answer from a dummy instead.
    /// </remarks>
    public static void AnswerThePausesWithDetailsFromTheReasonlessOnes(IScheduler fake)
    {
        A.CallTo(() => fake.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.PauseJobWith(A<JobKey>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.PauseTriggerGroupsWith(A<GroupMatcher<TriggerKey>>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.PauseJobGroupsWith(A<GroupMatcher<JobKey>>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.PauseAllWith(A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();

        // The key-set forms' defaults walk the set through the single-key *With members above.
        A.CallTo(() => fake.PauseTriggersWith(A<IReadOnlyCollection<TriggerKey>>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.PauseJobsWith(A<IReadOnlyCollection<JobKey>>._, A<PauseDetails>._, A<CancellationToken>._)).CallsBaseMethod();
    }
}
