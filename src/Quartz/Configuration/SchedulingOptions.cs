namespace Quartz;

public class SchedulingOptions
{
    /// <summary>
    /// Whether the existing scheduling data (with same identifiers) will be
    /// overwritten.
    /// </summary>
    /// <remarks>
    /// If false, and <see cref="IgnoreDuplicates" /> is not false, and jobs or
    /// triggers with the same names already exist as those in the file, an
    /// error will occur.
    /// </remarks>
    /// <seealso cref="IgnoreDuplicates" />
    public bool OverWriteExistingData { get; set; } = true;

    /// <summary>
    /// If true (and <see cref="OverWriteExistingData" /> is false) then any
    /// job/triggers encountered in this file that have names that already exist
    /// in the scheduler will be ignored, and no error will be produced.
    /// </summary>
    /// <seealso cref="OverWriteExistingData"/>
    public bool IgnoreDuplicates { get; set; }

    /// <summary>
    /// If true (and <see cref="OverWriteExistingData" /> is true) then any
    /// job/triggers encountered in this file that already exist is scheduler
    /// will be updated with start time relative to old trigger. Effectively
    /// new trigger's last fire time will be updated to old trigger's last fire time
    /// and trigger's next fire time will updated to be next from this last fire time.
    /// </summary>
    public bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }
}