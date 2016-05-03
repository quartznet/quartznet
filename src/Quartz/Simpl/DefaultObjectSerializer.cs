using System.IO;
#if BINARY_SERIALIZATION
using System.Runtime.Serialization.Formatters.Binary;
#else // BINARY_SERIALIZATION
using Newtonsoft.Json;
#endif // BINARY_SERIALIZATION

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// Default object serialization strategy that uses <see cref="BinaryFormatter" /> 
    /// under the hood.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class DefaultObjectSerializer : IObjectSerializer
    {
#if DataContractSerializer // Currently unused. Can be removed in the future, but leaving temporarily while confirming that JSON.NET serialization works well
        static Type[] KnownQuartzTypes = new Type[]
        {
            typeof(JobDataMap),
            typeof(Impl.JobDetailImpl),
            typeof(Impl.Triggers.AbstractTrigger),
            typeof(Impl.Triggers.CalendarIntervalTriggerImpl),
            typeof(Impl.Triggers.CronTriggerImpl),
            typeof(Impl.Triggers.DailyTimeIntervalTriggerImpl),
            typeof(Impl.Triggers.SimpleTriggerImpl),
            typeof(Impl.Calendar.AnnualCalendar),
            typeof(Impl.Calendar.BaseCalendar),
            typeof(Impl.Calendar.CronCalendar),
            typeof(Impl.Calendar.DailyCalendar),
            typeof(Impl.Calendar.HolidayCalendar),
            typeof(Impl.Calendar.MonthlyCalendar),
            typeof(Impl.Calendar.WeeklyCalendar),
            typeof(Impl.Matchers.AndMatcher<>),
            typeof(Impl.Matchers.GroupMatcher<>),
            typeof(Impl.Matchers.KeyMatcher<>),
            typeof(Impl.Matchers.NameMatcher<>),
            typeof(Impl.Matchers.NotMatcher<>),
            typeof(Impl.Matchers.OrMatcher<>),
            typeof(Impl.Matchers.StringMatcher<>),
            typeof(Util.StringKeyDirtyFlagMap),
            typeof(Util.DirtyFlagMap<,>)
        };
#endif // DataContractSerialization

        /// <summary>
        /// Serializes given object as bytes 
        /// that can be stored to permanent stores.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        public byte[] Serialize<T>(T obj) where T : class
        {
            using (MemoryStream ms = new MemoryStream())
            {
#if BINARY_SERIALIZATION
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
#else // BINARY_SERIALIZATION
                using (var sw = new StreamWriter(ms))
                {
                    var js = new JsonSerializer();
                    js.TypeNameHandling = TypeNameHandling.All;
                    js.PreserveReferencesHandling = PreserveReferencesHandling.All;
                    js.Serialize(sw, obj);
                }
#endif // BINARY_SERIALIZATION
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes object from byte array presentation.
        /// </summary>
        /// <param name="data">Data to deserialize object from.</param>
        public T DeSerialize<T>(byte[] data) where T : class
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
#if BINARY_SERIALIZATION
                BinaryFormatter bf = new BinaryFormatter();
                return (T)bf.Deserialize(ms);
#else // BINARY_SERIALIZATION
                using (var sr = new StreamReader(ms))
                {
                    var js = new JsonSerializer();
                    js.TypeNameHandling = TypeNameHandling.All;
                    js.PreserveReferencesHandling = PreserveReferencesHandling.All;
                    return (T)js.Deserialize(sr, typeof(T));
                }
#endif // BINARY_SERIALIZATION
            }
        }
    }
}