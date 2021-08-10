using EWSoftware.PDI;

namespace Quartz
{
    public interface IRecurrenceTrigger : ITrigger
    {
        /// <summary>
        /// Gets or sets the Recurrence Rule.
        /// </summary>
        /// <value>The RecurrencRule string</value>
        string? RecurrenceRule { get; set; }

        /// <summary>
        /// The Recurrence itself.
        /// </summary>
        Recurrence? Recurrence { get; set; }
    }
}
