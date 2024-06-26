using System.Threading.Tasks;

 namespace Quartz.Tests.Integration;

 public class RAMSchedulerTest : AbstractSchedulerTest
 {
     public RAMSchedulerTest() : base("memory", "default-serializer")
     {
     }

     protected override Task<IScheduler> CreateScheduler(string name, int threadPoolSize)
     {
         var config = SchedulerBuilder.Create("AUTO", name + "Scheduler");

         config.UseDefaultThreadPool(x =>
         {
             x.MaxConcurrency = threadPoolSize;
         });

         return config.BuildScheduler();
     }

     public RAMSchedulerTest(string provider) : base(provider, "default-serializer")
     {
     }
 }