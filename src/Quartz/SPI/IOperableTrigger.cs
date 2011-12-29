using System;

namespace Quartz.Spi
{
    /// <summary>
    /// Internal interface for managing triggers. This interface should not be used by the Quartz client.
    /// </summary>
    public interface IOperableTrigger : IMutableTrigger
    {
        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// Called when the <see cref="IScheduler" /> has decided to 'fire'
        /// the trigger (Execute the associated <see cref="IJob" />), in order to
        /// give the <see cref="ITrigger" /> a chance to update itself for its next
        /// triggering (if any).
        /// </remarks>
        /// <seealso cref="JobExecutionException" />
        void Triggered(ICalendar calendar);

        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called by the scheduler at the time a <see cref="ITrigger" /> is first
        /// added to the scheduler, in order to have the <see cref="ITrigger" />
        /// compute its first fire time, based on any associated calendar.
        /// </para>
        /// 
        /// <para>
        /// After this method has been called, <see cref="ITrigger.GetNextFireTimeUtc" />
        /// should return a valid answer.
        /// </para>
        /// </remarks>
        /// <returns> 
        /// The first time at which the <see cref="ITrigger" /> will be fired
        /// by the scheduler, which is also the same value <see cref="ITrigger.GetNextFireTimeUtc" />
        /// will return (until after the first firing of the <see cref="ITrigger" />).
        /// </returns>     
        DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar calendar);

        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// Called after the <see cref="IScheduler" /> has executed the
        /// <see cref="IJobDetail" /> associated with the <see cref="ITrigger" />
        /// in order to get the final instruction code from the trigger.
        /// </remarks>
        /// <param name="context">
        /// is the <see cref="IJobExecutionContext" /> that was used by the
        /// <see cref="IJob" />'s<see cref="IJob.Execute" /> method.</param>
        /// <param name="result">is the <see cref="JobExecutionException" /> thrown by the
        /// <see cref="IJob" />, if any (may be null).
        /// </param>
        /// <returns>
        /// One of the <see cref="SchedulerInstruction"/> members.
        /// </returns>
        /// <seealso cref="SchedulerInstruction.NoInstruction" />
        /// <seealso cref="SchedulerInstruction.ReExecuteJob" />
        /// <seealso cref="SchedulerInstruction.DeleteTrigger" />
        /// <seealso cref="SchedulerInstruction.SetTriggerComplete" />
        /// <seealso cref="Triggered" />
        SchedulerInstruction ExecutionComplete(IJobExecutionContext context, JobExecutionException result);

        /// <summary> 
        /// This method should not be used by the Quartz client.
        /// <para>
        /// To be implemented by the concrete classes that extend this class.
        /// </para>
        /// <para>
        /// The implementation should update the <see cref="ITrigger" />'s state
        /// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <see cref="ITrigger" />
        /// was created.
        /// </para>
        /// </summary>
        void UpdateAfterMisfire(ICalendar cal);

        /// <summary> 
        /// This method should not be used by the Quartz client.
        /// <para>
        /// The implementation should update the <see cref="ITrigger" />'s state
        /// based on the given new version of the associated <see cref="ICalendar" />
        /// (the state should be updated so that it's next fire time is appropriate
        /// given the Calendar's new settings). 
        /// </para>
        /// </summary>
        /// <param name="cal"> </param>
        /// <param name="misfireThreshold"></param>
        void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold);


        /// <summary>
        /// Validates whether the properties of the <see cref="IJobDetail" /> are
        /// valid for submission into a <see cref="IScheduler" />.
        /// </summary>
        void Validate();


        /// <summary> 
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// Usable by <see cref="IJobStore" />
        /// implementations, in order to facilitate 'recognizing' instances of fired
        /// <see cref="ITrigger" /> s as their jobs complete execution.
        /// </remarks>
        string FireInstanceId { get; set; }

        void SetNextFireTimeUtc(DateTimeOffset? value);

        void SetPreviousFireTimeUtc(DateTimeOffset? value);
    }
}