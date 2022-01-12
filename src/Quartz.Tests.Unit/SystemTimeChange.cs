using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Quartz.Impl;

namespace Quartz.Tests.Unit
{
    internal class SystemTimeChange
    {
        public static Boolean Ran;

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public Int16 wYear;
            public Int16 wMonth;
            public Int16 wDayOfWeek;
            public Int16 wDay;
            public Int16 wHour;
            public Int16 wMinute;
            public Int16 wSecond;
            public Int16 wMilliseconds;
        }

        [TestFixture]
        public class Testcase : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                Ran = true;
                return Task.CompletedTask;
            }
            public void Dispose()
            {
                SystemTime.Now = () => DateTime.Now;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern Boolean SetSystemTime(ref SYSTEMTIME st);

            private SYSTEMTIME GetSYSTEMTIME(DateTime dt)
            {
                return new SYSTEMTIME()
                {
                    wYear = (Int16)dt.Year,
                    wMonth = (Int16)dt.Month,
                    wDay = (Int16)dt.Day,
                    wDayOfWeek = (Int16)dt.DayOfWeek,
                    wHour = (Int16)dt.Hour,
                    wMinute = (Int16)dt.Minute,
                    wSecond = (Int16)dt.Second,
                    wMilliseconds = (Int16)dt.Millisecond,
                };
            }

            public async Task<Boolean> ChangingTime()
            {
                const Int32 interval = 2;
                const Int32 multiplier = 3;

                IScheduler scheduler = await new StdSchedulerFactory().GetScheduler();
                IJobDetail job = JobBuilder.Create<Testcase>().Build();
                ITrigger trigger = TriggerBuilder.Create()
                    .WithSimpleSchedule(
                        s => s.WithIntervalInSeconds(interval)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionFireNow()
                    )
                    .Build();

                await scheduler.ScheduleJob(job, trigger);
                await scheduler.Start();

                DateTime yesterDateTime = DateTime.Now.AddDays(-1);
                SYSTEMTIME yesterDay = GetSYSTEMTIME(yesterDateTime);
                SetSystemTime(ref yesterDay);

                if (DateTime.Now.Day != yesterDateTime.Day)
                {
                    Console.WriteLine("Could not set the system date/time.");
                    return false;
                }

                Ran = false;
                await Task.Delay(interval * multiplier * 1000);

                DateTime todayDateTime = DateTime.Now.AddDays(1);
                SYSTEMTIME today = GetSYSTEMTIME(todayDateTime);
                
                SetSystemTime(ref today);
                await Task.Delay(interval * multiplier * 1000);

                return Ran;
            }
            
            [Test]
            public async Task Main()
            {
                Assert.IsTrue(await ChangingTime());
            }
        }
    }
}
