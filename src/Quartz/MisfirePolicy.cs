
namespace Quartz
{
    ///<summary>
    /// Misfire policies.
    ///</summary>
    public struct MisfirePolicy
    {
        /// <summary>
        /// Instruction no set (yet).
        /// </summary>
        public const int InstructionNotSet = 0;

        /// <summary>
        /// Use smart policy.
        /// </summary>
        public const int SmartPolicy = 0;

        /// <summary>
        /// Misfire policy settings for SimpleTrigger.
        /// </summary>
        public struct SimpleTrigger
        {
            /// <summary> 
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="SimpleTrigger" /> wants to be fired
            /// now by <see cref="IScheduler" />.
            /// <p>
            /// <i>NOTE:</i> This instruction should typically only be used for
            /// 'one-shot' (non-repeating) Triggers. If it is used on a trigger with a
            /// repeat count > 0 then it is equivalent to the instruction 
            /// <see cref="RescheduleNowWithRemainingRepeatCount " />.
            /// </p>
            /// </summary>		
            public const int FireNow = 1;


            /// <summary>
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="SimpleTrigger" /> wants to be
            /// re-scheduled to 'now' (even if the associated <see cref="ICalendar" />
            /// excludes 'now') with the repeat count left as-is.   This does obey the
            /// <see cref="Trigger" /> end-time however, so if 'now' is after the
            /// end-time the <code>Trigger</code> will not fire again.
            /// <p>
            /// <i>NOTE:</i> Use of this instruction causes the trigger to 'forget'
            /// the start-time and repeat-count that it was originally setup with (this
            /// is only an issue if you for some reason wanted to be able to tell what
            /// the original values were at some later time).
            /// </p>
            /// 
            /// <p>
            /// <i>NOTE:</i> This instruction could cause the <see cref="Trigger" />
            /// to go to the 'COMPLETE' state after firing 'now', if all the
            /// repeat-fire-times where missed.
            /// </p>
            /// </summary>
            public const int RescheduleNowWithExistingRepeatCount = 2;

            /// <summary>
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="SimpleTrigger" /> wants to be
            /// re-scheduled to 'now' (even if the associated <see cref="ICalendar" />
            /// excludes 'now') with the repeat count set to what it would be, if it had
            /// not missed any firings. This does obey the <see cref="Trigger" /> end-time 
            /// however, so if 'now' is after the end-time the <see cref="Trigger" /> will 
            /// not fire again.
            /// 
            /// <p>
            /// <i>NOTE:</i> Use of this instruction causes the trigger to 'forget'
            /// the start-time and repeat-count that it was originally setup with (this
            /// is only an issue if you for some reason wanted to be able to tell what
            /// the original values were at some later time).
            /// </p>
            /// 
            /// <p>
            /// <i>NOTE:</i> This instruction could cause the <see cref="Trigger" />
            /// to go to the 'COMPLETE' state after firing 'now', if all the
            /// repeat-fire-times where missed.
            /// </p>
            /// </summary>
            public const int RescheduleNowWithRemainingRepeatCount = 3;

            /// <summary> 
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="SimpleTrigger" /> wants to be
            /// re-scheduled to the next scheduled time after 'now' - taking into
            /// account any associated <see cref="ICalendar" />, and with the
            /// repeat count set to what it would be, if it had not missed any firings.
            /// </summary>
            /// <remarks>
            /// <i>NOTE/WARNING:</i> This instruction could cause the <see cref="Trigger" />
            /// to go directly to the 'COMPLETE' state if all fire-times where missed.
            /// </remarks>
            public const int RescheduleNextWithRemainingCount = 4;

            /// <summary>
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="SimpleTrigger" /> wants to be
            /// re-scheduled to the next scheduled time after 'now' - taking into
            /// account any associated <see cref="ICalendar" />, and with the
            /// repeat count left unchanged.
            /// <p>
            /// <i>NOTE:</i> Use of this instruction causes the trigger to 'forget'
            /// the repeat-count that it was originally setup with (this is only an
            /// issue if you for some reason wanted to be able to tell what the original
            /// values were at some later time).
            /// </p>
            /// <p>
            /// <i>NOTE/WARNING:</i> This instruction could cause the <see cref="Trigger" />
            /// to go directly to the 'COMPLETE' state if all fire-times where missed.
            /// </p>
            /// </summary>
            public const int RescheduleNextWithExistingCount = 5;

        }

        /// <summary>
        /// misfire instructions for CronTrigger
        /// </summary>
        public struct CronTrigger
        {
            /// <summary>
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="CronTrigger" /> wants to be fired now
            /// by <see cref="IScheduler" />.
            /// </summary>
            public const int FireOnceNow = 1;

            /// <summary>
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire
            /// situation, the <see cref="CronTrigger" /> wants to have it's
            /// next-fire-time updated to the next time in the schedule after the
            /// current time (taking into account any associated <see cref="ICalendar" />,
            /// but it does not want to be fired now.
            /// </summary>
            public const int DoNothing = 2;

        }

        /// <summary>
        /// misfire instructions for NthIncludedDayTrigger
        /// </summary>
        public struct NthIncludedDayTrigger
        {
            /// <summary> 
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire situation, the
            /// <see cref="NthIncludedDayTrigger" /> wants to be fired now by the 
            /// <see cref="IScheduler" />
            /// </summary>
            public const int FireOnceNow = 1;

            /// <summary> 
            /// Instructs the <see cref="IScheduler" /> that upon a mis-fire situation, the
            /// <see cref="NthIncludedDayTrigger" /> wants to have 
            /// nextFireTime updated to the next time in the schedule after
            /// the current time, but it does not want to be fired now.
            /// </summary>
            public const int DoNothing = 2;

        }
    }

}
