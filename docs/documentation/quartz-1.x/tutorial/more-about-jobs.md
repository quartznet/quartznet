---

title: 'Lesson 3: More About Jobs & JobDetails'
---

As you've seen, jobs are rather easy to implement. There are just a few more things that you need to understand about
the nature of jobs, about the Execute(..) method of the IJob interface, and about JobDetails.

While a class that you implement is the actual "job", Quartz needs to be informed about various attributes
that you may wish the job to have. This is done via the JobDetail class, which was mentioned briefly in the previous section.
Software 'archaeologists' may be interested to know that in an older incarnation of Quartz for Java, the implementation of the
functionality of JobDetail was imposed upon the implementor of each Job class by having all of JobDetail's 'getter' methods on
the Job interface itself. This forced a cumbersome job of re-implementing virtually identical code on every Job class -
which was really dumb... thus JobDetail class was created.

Let's take a moment now to discuss a bit about the 'nature' of jobs and the life-cycle of job instances within Quartz.NET.
First lets take a look back at some of that snippet of code we saw in Lesson 1:

__Using Quartz.NET__

```csharp
    // construct a scheduler factory
    ISchedulerFactory schedFact = new StdSchedulerFactory();
    
    // get a scheduler
    IScheduler sched = schedFact.GetScheduler();
    sched.Start();
    
    // construct job info
    JobDetail jobDetail = new JobDetail("myJob", null, typeof(DumbJob));
    // fire every hour
    Trigger trigger = TriggerUtils.MakeHourlyTrigger();
    // start on the next even hour
    trigger.StartTime = TriggerUtils.GetEvenHourDate(DateTime.UtcNow);  
    trigger.Name = "myTrigger";
    sched.ScheduleJob(jobDetail, trigger); 
```

Now consider the job class _DumbJob_ defined as such:

```csharp
    public class DumbJob : IJob
    {
        public DumbJob() {
        }
    
        public void Execute(JobExecutionContext context)
        {
            Console.WriteLine("DumbJob is executing.");
        }
    }
```

Notice that we 'feed' the scheduler a JobDetail instance, and that it refers to the job to be executed by simply
providing the job's class. Each (and every) time the scheduler executes the job, it creates a new instance of the
class before calling its Execute(..) method. One of the ramifications of this behavior is the fact that jobs must
have a no-argument constructor. Another ramification is that it does not make sense to have data-members defined
on the job class - as their values would be 'cleared' every time the job executes.

You may now be wanting to ask "how can I provide properties/configuration for a Job instance?" and "how can I
keep track of a job's state between executions?" The answer to these questions are the same: the key is the JobDataMap,
which is part of the JobDetail object.

## JobDataMap

The JobDataMap can be used to hold any number of (serializable) objects which you wish to have made available
to the job instance when it executes. JobDataMap is an implementation of the IDictionary interface, and has some added convenience methods for storing and retrieving data of primitive types.

Here's some quick snippets of putting data into the JobDataMap prior to adding the job to the scheduler:

__Setting Values in a JobDataMap__

```csharp
    jobDetail.JobDataMap["jobSays"] = "Hello World!";
    jobDetail.JobDataMap["myFloatValue"] =  3.141f;
    jobDetail.JobDataMap["myStateData"] = new ArrayList(); 
```

Here's a quick example of getting data from the JobDataMap during the job's execution:

__Getting Values from a JobDataMap__

```csharp
    public class DumbJob : IJob
    {
        public void Execute(JobExecutionContext context)
        {
            string instName = context.JobDetail.Name;
            string instGroup = context.JobDetail.Group;
    
            JobDataMap dataMap = context.JobDetail.JobDataMap;
    
            string jobSays = dataMap.GetString("jobSays");
            float myFloatValue = dataMap.GetFloat("myFloatValue");
            ArrayList state = (ArrayList) dataMap["myStateData"];
            state.Add(DateTime.UtcNow);
    
            Console.WriteLine("Instance {0} of DumbJob says: {1}", instName, jobSays);
        }
    } 
```

If you use a persistent JobStore (discussed in the JobStore section of this tutorial) you should use some care
in deciding what you place in the JobDataMap, because the object in it will be serialized, and they therefore
become prone to class-versioning problems. Obviously standard .NET types should be very safe, but beyond that,
anytime someone changes the definition of a class for which you have serialized instances, care has to be taken
not to break compatibility. Optionally, you can put AdoJobStore and JobDataMap into a mode where only primitives
and strings can be stored in the map, thus eliminating any possibility of later serialization problems.

### Stateful vs. Non-Stateful Jobs

Triggers can also have JobDataMaps associated with them. This can be useful in the case where you have a Job that
is stored in the scheduler for regular/repeated use by multiple Triggers, yet with each independent triggering,
you want to supply the Job with different data inputs.

The JobDataMap that is found on the JobExecutionContext during Job execution serves as a convenience. It is a merge
of the JobDataMap found on the JobDetail and the one found on the Trigger, with the value in the latter overriding
any same-named values in the former.

Here's a quick example of getting data from the JobExecutionContext's merged JobDataMap during the job's execution:

__Getting Values from the JobExecutionContext convenience/merged JobDataMap__

```csharp
    public class DumbJob : IJob
    {
        public void Execute(JobExecutionContext context)
        {
            string instName = context.JobDetail.Name;
            string instGroup = context.JobDetail.Group;
    
            // Note the difference from the previous example
            JobDataMap dataMap = context.MergedJobDataMap;
    
            string jobSays = dataMap.GetString("jobSays");
            float myFloatValue = dataMap.GetFloat("myFloatValue");
            ArrayList state = (ArrayList) dataMap.Get("myStateData");
            state.Add(DateTime.UtcNow);
    
            Console.WriteLine("Instance {0} of DumbJob says: {1}", instName, jobSays);
        }
    } 
```

## StatefulJob

Now, some additional notes about a job's state data (aka JobDataMap): A Job instance can be defined as "stateful" or "non-stateful".
Non-stateful jobs only have their JobDataMap stored at the time they are added to the scheduler. This means that any changes made
to the contents of the job data map during execution of the job will be lost, and will not seen by the job the next time it executes.
You have probably guessed, a stateful job is just the opposite - its JobDataMap is re-stored after every execution of the job.
One side-effect of making a job stateful is that it cannot be executed concurrently. Or in other words: if a job is stateful, and
a trigger attempts to 'fire' the job while it is already executing, the trigger will block (wait) until the previous execution completes.

You 'mark' a Job as stateful by having it implement the IStatefulJob interface, rather than the IJob interface.

## Job 'Instances'

One final point on this topic that may or may not be obvious by now: You can create a single job class, and store many
'instance definitions' of it within the scheduler by creating multiple instances of JobDetails - each with its own set of properties
and JobDataMap - and adding them all to the scheduler.

When a trigger fires, the Job it is associated to is instantiated via the JobFactory configured on the Scheduler. The default
JobFactory simply calls Activator.CreateInstance behind the scenes on the job class.
You may want to create your own implementation of JobFactory to accomplish things such as having your application's IoC or
DI container produce/initialize the job instance.

## Other Attributes Of Jobs

Here's a quick summary of the other properties which can be defined for a job instance via the JobDetail object:

* Durable - if a job is non-durable, it is automatically deleted from the scheduler once there are no longer any active triggers associated with it.
* Volatile - if a job is volatile, it is not persisted between re-starts of the Quartz scheduler.
* RequestsRecovery - if a job "requests recovery", and it is executing during the time of a 'hard shutdown' of the scheduler (i.e. the process it is running within crashes, or the machine is shut off), then it is re-executed when the scheduler is started again. In this case, the JobExecutionContext.IsRecovering property will return true.
* JobListeners - a job can have a set of zero or more JobListeners associated with it. When the job executes, the listeners are notified. More discussion on JobListeners can be found in the section of this document that is dedicated to the topic of TriggerListeners & JobListeners.

## The Job.Execute(..) Method

Finally, we need to inform you of a few details of the IJob.Execute(..) method. The only type of exception
that you are allowed to throw from the execute method is the JobExecutionException. Because of this, you should generally wrap the entire
contents of the execute method with a 'try-catch' block. You should also spend some time looking at the documentation for the
JobExecutionException, as your job can use it to provide the scheduler various directives as to how you want the exception to be handled.
