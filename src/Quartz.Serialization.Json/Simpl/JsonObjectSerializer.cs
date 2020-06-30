using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Quartz.Impl.Calendar;
using Quartz.Spi;
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
        private JsonSerializer serializer = null!;

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
                    new CalendarConverter()
                },
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new DefaultContractResolver
                {
                    IgnoreSerializableInterface = true
                },
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.DateTimeOffset
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
        public T? DeSerialize<T>(byte[] obj) where T : class
        {
            try
            {
                using var ms = new MemoryStream(obj);
                using var sr = new StreamReader(ms);
                return (T?) serializer.Deserialize(sr, typeof(T));
            }
            catch (JsonSerializationException e)
            {
                var json = Encoding.UTF8.GetString(obj);
                throw new JsonSerializationException("could not deserialize JSON: " + json, e);
            }
        }

        protected class StringKeyDirtyFlagMapConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                var map = (StringKeyDirtyFlagMap) value!;
                serializer.Serialize(writer, map.WrappedMap);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                IDictionary<string, object> innerMap = serializer.Deserialize<IDictionary<string, object>>(reader)!;
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
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                var cronExpression = (CronExpression) value!;
                writer.WriteStartObject();

                writer.WritePropertyName("$type");
                writer.WriteValue(value!.GetType().AssemblyQualifiedNameWithoutVersion());

                writer.WritePropertyName("CronExpression");
                writer.WriteValue(cronExpression.CronExpressionString);

                writer.WritePropertyName("TimeZoneId");
                writer.WriteValue(cronExpression.TimeZone?.Id);

                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                JObject jObject = JObject.Load(reader);
                var cronExpressionString = jObject["CronExpression"]!.Value<string>();

                var cronExpression = new CronExpression(cronExpressionString);
                cronExpression.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"]!.Value<string>());
                return cronExpression;
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(CronExpression);
            }
        }

        protected class CalendarConverter : JsonConverter
        {
            private static readonly Dictionary<string, ICalendarConverter> converters = new Dictionary<string, ICalendarConverter>
            {
                {typeof(BaseCalendar).AssemblyQualifiedNameWithoutVersion(), new BaseCalendarConverter()},
                {typeof(AnnualCalendar).AssemblyQualifiedNameWithoutVersion(), new AnnualCalendarConverter()},
                {typeof(CronCalendar).AssemblyQualifiedNameWithoutVersion(), new CronCalendarConverter()},
                {typeof(DailyCalendar).AssemblyQualifiedNameWithoutVersion(), new DailyCalendarConverter()},
                {typeof(HolidayCalendar).AssemblyQualifiedNameWithoutVersion(), new HolidayCalendarConverter()},
                {typeof(MonthlyCalendar).AssemblyQualifiedNameWithoutVersion(), new MonthlyCalendarConverter()},
                {typeof(WeeklyCalendar).AssemblyQualifiedNameWithoutVersion(), new WeeklyCalendarConverter()},
            };

            protected virtual ICalendarConverter GetCalendarConverter(string typeName)
            {
                if (!converters.TryGetValue(typeName, out var converter))
                {
                    throw new ArgumentException("don't know how to handle " + typeName);
                }

                return converter;
            }
            
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                var calendar = (BaseCalendar) value!;

                writer.WriteStartObject();
                writer.WritePropertyName("$type");
                var type = value!.GetType().AssemblyQualifiedNameWithoutVersion();
                writer.WriteValue(type);

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

                GetCalendarConverter(type).WriteCalendarFields(writer, calendar);

                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                JObject jObject = JObject.Load(reader);
                string type = jObject["$type"]!.Value<string>();

                var calendarConverter = GetCalendarConverter(type);
                BaseCalendar target = calendarConverter.Create(jObject);
                target.Description = jObject["Description"]!.Value<string>();
                target.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"]!.Value<string>());
                var baseCalendar = jObject["BaseCalendar"]!.Value<JObject>();
                if (baseCalendar != null)
                {
                    var baseCalendarType = Type.GetType(baseCalendar["$type"]!.Value<string>(), true);
                    var o = baseCalendar.ToObject(baseCalendarType!, serializer);
                    target.CalendarBase = (ICalendar?) o;
                }

                calendarConverter.PopulateFieldsToCalendarObject(target, jObject);

                return target;
            }

            public override bool CanConvert(Type objectType)
            {
                return typeof(ICalendar).IsAssignableFrom(objectType);
            }
        }

        protected interface ICalendarConverter
        {
            void WriteCalendarFields(JsonWriter writer, BaseCalendar value);

            void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject);

            BaseCalendar Create(JObject value);
        }
        
        protected class BaseCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
            }

            public BaseCalendar Create(JObject value)
            {
                return new BaseCalendar();
            }
        }

        protected class AnnualCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in ((AnnualCalendar) value).DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
                var annualCalendar = (AnnualCalendar) value;
                var excludedDates = jObject["ExcludedDays"]!.Values<DateTimeOffset>();
                foreach (var date in excludedDates)
                {
                    annualCalendar.SetDayExcluded(date.DateTime, true);
                }
            }

            public BaseCalendar Create(JObject value)
            {
                return new AnnualCalendar();
            }
        }

        protected class CronCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                writer.WritePropertyName("CronExpressionString");
                writer.WriteValue(((CronCalendar) value).CronExpression?.CronExpressionString);
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
            }

            public BaseCalendar Create(JObject value)
            {
                string cronExpression = value["CronExpressionString"]!.Value<string>();
                return new CronCalendar(cronExpression);
            }
        }

        protected class DailyCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                var calendar = (DailyCalendar) value;

                writer.WritePropertyName("InvertTimeRange");
                writer.WriteValue(calendar.InvertTimeRange);

                writer.WritePropertyName("RangeStartingTime");
                writer.WriteValue(calendar.RangeStartingTime);

                writer.WritePropertyName("RangeEndingTime");
                writer.WriteValue(calendar.RangeEndingTime);
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
                ((DailyCalendar) value).InvertTimeRange = jObject["InvertTimeRange"]!.Value<bool>();
            }

            public BaseCalendar Create(JObject value)
            {
                var rangeStartingTime = value["RangeStartingTime"]!.Value<string>();
                var rangeEndingTime = value["RangeEndingTime"]!.Value<string>();
                return new DailyCalendar(null, rangeStartingTime, rangeEndingTime);
            }
        }

        protected class HolidayCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                writer.WritePropertyName("ExcludedDates");
                writer.WriteStartArray();
                foreach (var day in ((HolidayCalendar) value).ExcludedDates)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
                var calendar = (HolidayCalendar) value;
                var excludedDates = jObject["ExcludedDates"]!.Values<DateTimeOffset>();
                foreach (var date in excludedDates)
                {
                    calendar.AddExcludedDate(date.DateTime);
                }
            }

            public BaseCalendar Create(JObject value)
            {
                return new HolidayCalendar();
            }
        }

        protected class MonthlyCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in ((MonthlyCalendar) value).DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
                ((MonthlyCalendar) value).DaysExcluded = jObject["ExcludedDays"]!.Values<bool>().ToArray();
            }

            public BaseCalendar Create(JObject value)
            {
                return new MonthlyCalendar();
            }
        }

        protected class WeeklyCalendarConverter : ICalendarConverter
        {
            public void WriteCalendarFields(JsonWriter writer, BaseCalendar value)
            {
                writer.WritePropertyName("ExcludedDays");
                writer.WriteStartArray();
                foreach (var day in ((WeeklyCalendar) value).DaysExcluded)
                {
                    writer.WriteValue(day);
                }
                writer.WriteEndArray();
            }

            public void PopulateFieldsToCalendarObject(BaseCalendar value, JObject jObject)
            {
                ((WeeklyCalendar) value).DaysExcluded = jObject["ExcludedDays"]!.Values<bool>().ToArray();
            }

            public BaseCalendar Create(JObject value)
            {
                return new WeeklyCalendar();
            }
        }
    }
}