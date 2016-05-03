using Newtonsoft.Json;
using System;

namespace Quartz.Spi
{
    /// <summary>
    /// Result holder for trigger firing event.
    /// </summary>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    public class TriggerFiredResult
    {
        // JsonProperty attributes are used since Json.Net's default behavior is to serialize public members and the properties wrapping these fields are read-only
        [JsonProperty] private readonly TriggerFiredBundle triggerFiredBundle;
        [JsonProperty] private readonly Exception exception;

        ///<summary>
        /// Constructor.
        ///</summary>
        ///<param name="triggerFiredBundle"></param>
        public TriggerFiredResult(TriggerFiredBundle triggerFiredBundle)
        {
            this.triggerFiredBundle = triggerFiredBundle;
        }

        ///<summary>
        /// Constructor.
        ///</summary>
        public TriggerFiredResult(Exception exception)
        {
            this.exception = exception;
        }

        ///<summary>
        /// Bundle.
        ///</summary>
        public TriggerFiredBundle TriggerFiredBundle
        {
            get { return triggerFiredBundle; }
        }

        /// <summary>
        /// Possible exception.
        /// </summary>
        public Exception Exception
        {
            get { return exception; }
        }
    }
}