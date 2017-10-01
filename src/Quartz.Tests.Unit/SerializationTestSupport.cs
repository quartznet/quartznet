#region License

/* 
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */

#endregion

using System;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using Quartz.Spi;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Base class for unit tests that wish to verify 
    /// backwards compatibility of serialization with earlier versions
    /// of Quartz.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public abstract class SerializationTestSupport
    {
        private readonly IObjectSerializer serializer;

        public SerializationTestSupport(Type serializerType)
        {
            serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
            serializer.Initialize();
        }

        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected abstract object GetTargetObject();

        /// <summary>
        /// Get the Quartz versions for which we should verify
        /// serialization backwards compatibility.
        /// </summary>
        /// <returns></returns>
        protected abstract string[] GetVersions();

        /// <summary>
        /// Verify that the target object and the object we just deserialized 
        /// match.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deserialized"></param>
        protected abstract void VerifyMatch(object target, object deserialized);

        /// <summary>
        /// Test that we can successfully deserialize our target
        /// class for all of the given Quartz versions. 
        /// </summary>
        [Test]
        [Ignore("Currently no working implementation for serialization testing")]
        public void TestSerialization()
        {
            object targetObject = GetTargetObject();

            for (int i = 0; i < GetVersions().Length; i++)
            {
                string version = GetVersions()[i];

                VerifyMatch(targetObject, Deserialize(version, targetObject.GetType()));
            }
        }

        /// <summary>
        ///  Deserialize the target object from disk.
        /// </summary>
        /// <param name="version"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        protected object Deserialize(string version, Type type)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Use this method in the future to generate other versions of
        /// of the serialized object file.
        /// </summary>
        [Test]
        public void WriteJobDataFile()
        {
            Assembly asm = GetType().GetTypeInfo().Assembly;
            Version info = asm.GetName().Version;

            string version = info.ToString();
            object obj = GetTargetObject();

            string fileName = GetSerializedFileName(version, obj.GetType());
            using (FileStream fs = new FileStream(fileName, FileMode.Create))
            {
                var bytes = serializer.Serialize(obj);
                fs.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// Generate the expected name of the serialized object file.
        /// </summary>
        /// <param name="version"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static string GetSerializedFileName(string version, Type type)
        {
            string className = type.Name;
            int index = className.LastIndexOf(".");
            index = index < 0 ? 0 : index + 1;

            return $"{className.Substring(index)}-{version}.ser";
        }
    }
}