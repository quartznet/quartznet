using System;

namespace Quartz.Spi
{
    /// <summary>
    /// Result holder for trigger firing event.
    /// </summary>
    [Serializable]
    public class TriggerFiredResult
    {
        // JsonProperty attributes are used since Json.Net's default behavior is to serialize public members and the properties wrapping these fields are read-only

        ///<summary>
        /// Constructor.
        ///</summary>
        ///<param name="triggerFiredBundle"></param>
        public TriggerFiredResult(TriggerFiredBundle triggerFiredBundle)
        {
            TriggerFiredBundle = triggerFiredBundle;
        }

        ///<summary>
        /// Constructor.
        ///</summary>
        public TriggerFiredResult(Exception exception)
        {
            Exception = exception;
        }

        ///<summary>
        /// Bundle.
        ///</summary>
        public TriggerFiredBundle TriggerFiredBundle { get; }

        /// <summary>
        /// Possible exception.
        /// </summary>
        public Exception Exception { get; }
    }
}