using System;
using System.Collections.Generic;
using System.Web.Mvc;

using Quartz.Impl;

namespace Quartz.Web.Controllers
{
    [HandleError]
    public class SchedulerController : BaseController
    {

        public ActionResult Index()
        {
            AddSchedulerInfoToViewData();
            return View();
        }

        public ActionResult Start(string schedulerName)
        {
            var scheduler = GetCurrentScheduler(schedulerName);
            scheduler.Start();
            ViewData["Message"] = string.Format("Scheduler {0} started successfully", schedulerName);
            return View();
        }

        public ActionResult StandBy(string schedulerName)
        {
            var scheduler = GetCurrentScheduler(schedulerName);
            scheduler.Standby();
            ViewData["Message"] = string.Format("Scheduler {0} set to stand-by mode successfully", schedulerName);
            return View();
        }

        public ActionResult Shutdown(string schedulerName)
        {
            var scheduler = GetCurrentScheduler(schedulerName);
            scheduler.Shutdown();
            ViewData["Message"] = string.Format("Scheduler {0} was shutdown successfully", schedulerName);
            return View();
        }

        private void AddSchedulerInfoToViewData()
        {
            var schedulers = new StdSchedulerFactory().AllSchedulers;
            ViewData["Schedulers"] = schedulers;

            IScheduler activeScheduler = GetCurrentScheduler();
            ViewData["ActiveScheduler"] = activeScheduler;
            ViewData["SchedulerName"] = activeScheduler.SchedulerName;
            ViewData["SchedulerMetaData"] = activeScheduler.GetMetaData();


            IList<JobExecutionContext> executingJobs = activeScheduler.GetCurrentlyExecutingJobs();
            ViewData["CurrentlyExecutingJobs"] = executingJobs;
            string[] calendars = activeScheduler.GetCalendarNames();

            IList<IJobListener> jobListeners = activeScheduler.GlobalJobListeners;
            ViewData["JobListeners"] = jobListeners;


            // The section commented out below is not currently used, but may be used to show triggers that have been
            // added to jobs

            /* List triggerListeners = choosenScheduler.getGlobalTriggerListeners();
	for (Iterator iter = triggerListeners.iterator(); iter.hasNext();) {
		TriggerListener triggerListener = (TriggerListener) iter.next();
		ListenerForm listenerForm = new ListenerForm();
		listenerForm.setListenerName(triggerListener.getName());
		listenerForm.setListenerClass(triggerListener.getClass().getName());
		schedForm.getGlobalJobListeners().add(listenerForm);
	}
	
	Set jobListenerNames = choosenScheduler.getJobListenerNames();
	for (Iterator iter = jobListenerNames.iterator(); iter.hasNext();) {
		JobListener jobListener = choosenScheduler.getJobListener((String) iter.next());
		ListenerForm listenerForm = new ListenerForm();
		listenerForm.setListenerName(jobListener.getName());
		listenerForm.setListenerClass(jobListener.getClass().getName());
		schedForm.getRegisteredJobListeners().add(listenerForm);
	}
	
	Set triggerListenerNames = choosenScheduler.getTriggerListenerNames();
	for (Iterator iter = triggerListenerNames.iterator(); iter.hasNext();) {
		TriggerListener triggerListener = choosenScheduler.getTriggerListener((String) iter.next());
		ListenerForm listenerForm = new ListenerForm();
		listenerForm.setListenerName(triggerListener.getName());
		listenerForm.setListenerClass(triggerListener.getClass().getName());
		schedForm.getRegisteredTriggerListeners().add(listenerForm);
	}

	List schedulerListeners = choosenScheduler.getSchedulerListeners();
	for (Iterator iter = schedulerListeners.iterator(); iter.hasNext();) {
		SchedulerListener schedulerListener = (SchedulerListener) iter.next();
		ListenerForm listenerForm = new ListenerForm();
		listenerForm.setListenerClass(schedulerListener.getClass().getName());
		schedForm.getSchedulerListeners().add(listenerForm);
	}

	*/
        }

        public const string CURRENT_SCHEDULER_PROP = "currentScheduler";

        public IScheduler createSchedulerAndUpdateApplicationContext(string schedulerName)
        {
            IScheduler currentScheduler = null;

            try
            {
                if (!string.IsNullOrEmpty(schedulerName))
                {
                    currentScheduler = new StdSchedulerFactory().GetScheduler(schedulerName);
                }
                else
                {
                    currentScheduler = StdSchedulerFactory.DefaultScheduler;
                }

                this.ControllerContext.HttpContext.Application[CURRENT_SCHEDULER_PROP] = currentScheduler;
            }
            catch (SchedulerException e)
            {
                Log.Error("Problem creating scheduler", e);
            }

            return currentScheduler;
        }

        public IScheduler GetCurrentScheduler(String schedulerName)
        {

            var currentScheduler = (IScheduler)ControllerContext.HttpContext.Application[CURRENT_SCHEDULER_PROP];
            if (currentScheduler == null)
            {
                currentScheduler = createSchedulerAndUpdateApplicationContext(schedulerName);
            }
            return currentScheduler;
        }


        public IScheduler GetCurrentScheduler()
        {
            return GetCurrentScheduler(null);
        }
    }
}