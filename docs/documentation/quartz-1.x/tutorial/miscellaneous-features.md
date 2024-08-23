---

title:  'Lesson 12: Miscellaneous Features of Quartz'
---

## Plug-Ins

Quartz provides an interface (ISchedulerPlugin) for plugging-in additional functionality.

Plugins that ship with Quartz to provide various utility capabilities can be found documented in the Quartz.Plugins namespace.
They provide functionality such as auto-scheduling of jobs upon scheduler startup, logging a history of job and trigger events,
and ensuring that the scheduler shuts down cleanly when the virtual machine exits.

## JobFactory

When a trigger fires, the Job it is associated to is instantiated via the JobFactory configured on the Scheduler.
The default JobFactory simply activates a new instance of the job class. You may want to create your own implementation
of JobFactory to accomplish things such as having your application's IoC or DI container produce/initialize the job instance.

See the IJobFactory interface, and the associated Scheduler.SetJobFactory(fact) method.

## 'Factory-Shipped' Jobs

Quartz also provides a number of utility Jobs that you can use in your application for doing things like sending
e-mails and invoking remote objects. These out-of-the-box Jobs can be found documented in the Quartz.Jobs namespace.
