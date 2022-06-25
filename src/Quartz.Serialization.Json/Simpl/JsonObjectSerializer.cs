using System.Collections.Generic;
using System.IO;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Quartz.Converters;
using Quartz.Spi;

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
            if (serializer is null)
            {
                ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
            }

            using var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms))
            {
                serializer.Serialize(sw, obj, typeof(object));
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Deserializes object from byte array presentation.
        /// </summary>
        /// <param name="obj">Data to deserialize object from.</param>
        public T? DeSerialize<T>(byte[] obj) where T : class
        {
            if (serializer is null)
            {
                ThrowHelper.ThrowInvalidOperationException("The serializer hasn't been initialized, did you forget to call Initialize()?");
            }

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

        public static void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
        {
            CalendarConverter.AddCalendarConverter<TCalendar>(serializer);
        }
    }
}