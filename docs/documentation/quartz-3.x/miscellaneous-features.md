---

title:  'Miscellaneous Features'
---

## Plug-Ins

The `ISchedulerPlugin` interface plugs additional functionality into Quartz.

The plugins that ship with Quartz are in the `Quartz.Plugins` namespace. They auto-schedule jobs at scheduler startup, log a history of job and trigger events, and shut the scheduler down cleanly when the virtual machine exits.

## JobFactory

When a trigger fires, the Scheduler's JobFactory instantiates the associated Job. The default JobFactory activates a new instance of the job class. Write your own, for example, to have your application's IoC or DI container create and initialize the job instance.

See the `IJobFactory` interface and the `IScheduler.JobFactory` setter property.

::: tip
Since Quartz 3.1 there is [built-in Microsoft Dependency Injection integration](./packages/microsoft-di-integration), which also lets you use other IoC containers.
:::

## 'Factory-Shipped' Jobs

The [Quartz.Jobs NuGet package](https://www.nuget.org/packages/Quartz.Jobs) has utility jobs, in the `Quartz.Jobs` namespace, for tasks such as sending e-mails and invoking remote objects.
