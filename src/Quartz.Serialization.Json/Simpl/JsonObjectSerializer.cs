using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Quartz.Impl.Calendar;
using Quartz.Spi;
using System.Reflection;
using System.Text;
using Quartz.Util;

namespace Quartz.Simpl
{
    /// <summary>
    /// Default object serialization strategy that uses <see cref="JsonSerializer" /> 
    /// under the hood.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class JsonObjectSerializer : IObjectSerializer
    {
        private JsonSerializer serializer;

        public void Initialize()
        {
            serializer = JsonSerializer.Create(CreateSerializerSettings());
        }

        protected virtual JsonSerializerSettings CreateSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new NameValueCollectionConverter(),
                    new StringKeyDirtyFlagMapConverter(),
                    new CronExpressionConverter(),
                    new AnnualCalendarConverter(),
                    new BaseCalendarConverter(),
                    new CronCalendarConverter(),
                    new DailyCalendarConverter(),
                    new HolidayCalendarConverter(),
                    new MonthlyCalendarConverter(),
                    new WeeklyCalendarConverter()
                },
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameHandling = TypeNameHandling.All,
                ContractResolver = new DefaultContractResolver
                {
#if BINARY_SERIALIZATION
                    IgnoreSerializableAttribute = true,
                    IgnoreSerializableInterface = true
#endif
                },
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        /// <summary>
        /// Serializes given object as bytes 
        /// that can be stored to permanent stores.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        public byte[] Serialize<T>(T obj) where T : class
        {
            using (var ms = new MemoryStream())
            {
                using (var sw = new StreamWriter(ms))
                {
                    serializer.Serialize(sw, obj, typeof(object));
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes object from byte array presentation.
        /// </summary>
        /// <param name="obj">Data to deserialize object from.</param>
        public T DeSerialize<T>(byte[] obj) where T : class
        {
            try
            {
                using (var ms = new MemoryStream(obj))
                {
                    using (var sr = new StreamReader(ms))
                    {
                        return (T) serializer.Deserialize(sr, typeof(T));
                    }
                }
            }
            catch (JsonSerializationException e)
            {
                var json = Encoding.UTF8.GetString(obj);
                throw new JsonSerializationException("could not deserialize JSON: " + json, e);
            }
        }

        protected class StringKeyDirtyFlagMapConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var map = (StringKeyDirtyFlagMap) value;
                serializer.Serialize(writer, map.WrappedMap);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                IDictionary<string, object> innerMap = serializer.Deserialize<IDictionary<string, object>>(reader);
                JobDataMap map = new JobDataMap(innerMap);
                return map;
            }

            public override bool CanConvert(Type objectType)
            {
                return typeof(StringKeyDirtyFlagMap).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
            }
        }

        protected class CronExpressionConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var cronExpression = (CronExpression) value;
                writer.WriteStartObject();

                writer.WritePropertyName("$type");
                writer.WriteValue(value.GetType().AssemblyQualifiedNameWithoutVersion());

                writer.WritePropertyName("CronExpression");
                writer.WriteValue(cronExpression.CronExpressionString);

                writer.WritePropertyName("TimeZoneId");
                writer.WriteValue(cronExpression.TimeZone?.Id);

                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JObject jObject = JObject.Load(reader);
                var cronExpressionString = jObject["CronExpression"].Value<string>();

                var cronExpression = new CronExpression(cronExpressionString);
                cronExpression.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"].Value<string>());
                return cronExpression;
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(CronExpression);
            }
        }

        protected abstract class CalendarConverter<TCalendar> : JsonConverter where TCalendar : BaseCalendar
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var calendar = (TCalendar) value;

                writer.WriteStartObject();
                writer.WritePropertyName("$type");
                writer.WriteValue(value.GetType().AssemblyQualifiedNameWithoutVersion());

                writer.WritePropertyName("Description");
                writer.WriteValue(calendar.Description);

                writer.WritePropertyName("TimeZoneId");
                writer.WriteValue(calendar.TimeZone?.Id);

                writer.WritePropertyName("BaseCalendar");
                if (calendar.CalendarBase != null)
                {
                    serializer.Serialize(writer, calendar.CalendarBase, calendar.CalendarBase.GetType());
                }
                else
                {
                    writer.WriteNull();
                }

                WriteCalendarFields(writer, calendar);

                writer.WriteEndObject();
            }

            protected abstract void WriteCalendarFields(JsonWriter writer, TCalendar value);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JObject jObject = JObject.Load(reader);

                TCalendar target = Create(jObject);
                target.Description = jObject["Description"].Value<string>();
                target.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"].Value<string>());
                var baseCalendar = jObject["BaseCalendar"].Value<JObject>();
                if (baseCalendar != null)
                {
                    var type = Type.GetType(baseCalendar["$type"].Value<string>(), true);
                    var o = baseCalendar.ToObject(type, serializer);
                    target.CalendarBase = (ICalendar) o;
                }

                PopulateFieldsToCalendarObject(target, jObject);

                return target;
            }

            protected abstract void PopulateFieldsToCalendarObject(TCalendar value, JObject jObject);

            protected abstract TCalendar Create(JObject value);

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(TCalendar);
            }
        }

        protected class BaseCalendarConverter : CalendarConverter<BaseCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
            }

            protected override void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
            }

            protected override BaseCalendar Create(JObject value)
            {
                return new BaseCalendar();
            }
        }

        protected class AnnualCalendarConverter : CalendarConverter<AnnualCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, AnnualCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in value.DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            protected override void PopulateFieldsToCalendarObject(AnnualCalendar value, JObject jObject)
            {
                var excludedDates = jObject["ExcludedDays"].Values<DateTime>();
                value.DaysExcluded = new SortedSet<DateTime>(excludedDates);
            }

            protected override AnnualCalendar Create(JObject value)
            {
                return new AnnualCalendar();
            }
        }

        protected class CronCalendarConverter : CalendarConverter<CronCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, CronCalendar value)
            {
                writer.WritePropertyName("CronExpressionString");
                writer.WriteValue(value.CronExpression?.CronExpressionString);
            }

            protected override void PopulateFieldsToCalendarObject(CronCalendar value, JObject jObject)
            {
            }

            protected override CronCalendar Create(JObject value)
            {
                string cronExpression = value["CronExpressionString"].Value<string>();
                return new CronCalendar(cronExpression);
            }
        }

        protected class DailyCalendarConverter : CalendarConverter<DailyCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, DailyCalendar value)
            {
                writer.WritePropertyName("InvertTimeRange");
                writer.WriteValue(value.InvertTimeRange);

                writer.WritePropertyName("RangeStartingTime");
                writer.WriteValue(value.RangeStartingTime);

                writer.WritePropertyName("RangeEndingTime");
                writer.WriteValue(value.RangeEndingTime);
            }

            protected override void PopulateFieldsToCalendarObject(DailyCalendar value, JObject jObject)
            {
                value.InvertTimeRange = jObject["InvertTimeRange"].Value<bool>();
            }

            protected override DailyCalendar Create(JObject value)
            {
                var rangeStartingTime = value["RangeStartingTime"].Value<string>();
                var rangeEndingTime = value["RangeEndingTime"].Value<string>();
                return new DailyCalendar(null, rangeStartingTime, rangeEndingTime);
            }
        }

        protected class HolidayCalendarConverter : CalendarConverter<HolidayCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, HolidayCalendar value)
            {
                writer.WritePropertyName("ExcludedDates");
                writer.WriteStartArray();
                foreach (var day in value.ExcludedDates)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            protected override void PopulateFieldsToCalendarObject(HolidayCalendar value, JObject jObject)
            {
                var ecludedDates = jObject["ExcludedDates"].Values<DateTime>();
                foreach (var date in ecludedDates)
                {
                    value.AddExcludedDate(date);
                }
            }

            protected override HolidayCalendar Create(JObject value)
            {
                return new HolidayCalendar();
            }
        }

        protected class MonthlyCalendarConverter : CalendarConverter<MonthlyCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, MonthlyCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in value.DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            protected override void PopulateFieldsToCalendarObject(MonthlyCalendar value, JObject jObject)
            {
                value.DaysExcluded = jObject["ExcludedDays"].Values<bool>().ToArray();
            }

            protected override MonthlyCalendar Create(JObject value)
            {
                return new MonthlyCalendar();
            }
        }

        protected class WeeklyCalendarConverter : CalendarConverter<WeeklyCalendar>
        {
            protected override void WriteCalendarFields(JsonWriter writer, WeeklyCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in value.DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            protected override void PopulateFieldsToCalendarObject(WeeklyCalendar value, JObject jObject)
            {
                value.DaysExcluded = jObject["ExcludedDays"].Values<bool>().ToArray();
            }

            protected override WeeklyCalendar Create(JObject value)
            {
                return new WeeklyCalendar();
            }
        }
    }
}