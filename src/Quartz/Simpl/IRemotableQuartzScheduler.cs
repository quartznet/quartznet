using System;
using System.Collections;

using Nullables;

using Quartz;
using Quartz.Collection;
using Quartz.Core;

namespace Quartz.Simpl
{
	public interface IRemotableQuartzScheduler
	{
		string SchedulerName { get; }
		string SchedulerInstanceId { get; }
		SchedulerContext SchedulerContext { get; }
		bool InStandbyMode { get; }
		bool IsShutdown { get; }
		string Version { get; }
		Type JobStoreClass { get; }
		Type ThreadPoolClass { get; }
		int ThreadPoolSize { get; }
		IList CurrentlyExecutingJobs { get; }
		IList GlobalJobListeners { get; }
		ISet JobListenerNames { get; }
		IList GlobalTriggerListeners { get; }
		ISet TriggerListenerNames { get; }
		IList SchedulerListeners { get; }

		/// <summary>
		/// Starts this instance.
		/// </summary>
		void Start();

		/// <summary>
		/// Standbies this instance.
		/// </summary>
		void Standby();

		/// <summary>
		/// Shutdowns this instance.
		/// </summary>
		void Shutdown();

		void Shutdown(bool waitForJobsToComplete);

		NullableDateTime RunningSince { get; }

		int NumJobsExecuted { get; }

		bool SupportsPersistence { get; }

		DateTime ScheduleJob(SchedulingContext ctxt, JobDetail jobDetail, Trigger trigger);

		DateTime ScheduleJob(SchedulingContext ctxt, Trigger trigger);

		void AddJob(SchedulingContext ctxt, JobDetail jobDetail, bool replace);

		bool DeleteJob(SchedulingContext ctxt, string jobName, string groupName);

		bool UnscheduleJob(SchedulingContext ctxt, string triggerName, string groupName);

		NullableDateTime RescheduleJob(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger);


		void TriggerJob(SchedulingContext ctxt, string jobName, string groupName, JobDataMap data);

		void TriggerJobWithVolatileTrigger(SchedulingContext ctxt, string jobName, string groupName, JobDataMap data);

		void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		void PauseTriggerGroup(SchedulingContext ctxt, string groupName);

		void PauseJob(SchedulingContext ctxt, string jobName, string groupName);

		void PauseJobGroup(SchedulingContext ctxt, string groupName);

		void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		void ResumeTriggerGroup(SchedulingContext ctxt, string groupName);

		ISet GetPausedTriggerGroups(SchedulingContext ctxt);

		void ResumeJob(SchedulingContext ctxt, string jobName, string groupName);

		void ResumeJobGroup(SchedulingContext ctxt, string groupName);

		void PauseAll(SchedulingContext ctxt);

		void ResumeAll(SchedulingContext ctxt);

		string[] GetJobGroupNames(SchedulingContext ctxt);

		string[] GetJobNames(SchedulingContext ctxt, string groupName);

		Trigger[] GetTriggersOfJob(SchedulingContext ctxt, string jobName, string groupName);

		string[] GetTriggerGroupNames(SchedulingContext ctxt);

		string[] GetTriggerNames(SchedulingContext ctxt, string groupName);

		JobDetail GetJobDetail(SchedulingContext ctxt, string jobName, string jobGroup);

		Trigger GetTrigger(SchedulingContext ctxt, string triggerName, string triggerGroup);

		TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string triggerGroup);

		void AddCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replace, bool updateTriggers);

		bool DeleteCalendar(SchedulingContext ctxt, string calName);

		ICalendar GetCalendar(SchedulingContext ctxt, string calName);

		string[] GetCalendarNames(SchedulingContext ctxt);

		void AddGlobalJobListener(IJobListener jobListener);

		void AddJobListener(IJobListener jobListener);

		bool RemoveGlobalJobListener(IJobListener jobListener);

		bool RemoveJobListener(string name);

		IJobListener GetJobListener(string name);

		void AddGlobalTriggerListener(ITriggerListener triggerListener);

		void AddTriggerListener(ITriggerListener triggerListener);

		bool RemoveGlobalTriggerListener(ITriggerListener triggerListener);

		bool RemoveTriggerListener(string name);

		ITriggerListener GetTriggerListener(string name);

		void AddSchedulerListener(ISchedulerListener schedulerListener);

		bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

		bool Interrupt(SchedulingContext ctxt, string jobName, string groupName);
	}
}
