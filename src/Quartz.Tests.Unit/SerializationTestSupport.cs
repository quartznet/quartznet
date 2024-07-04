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

using System.Reflection;

using Quartz.Spi;

namespace Quartz.Tests.Unit;

public abstract class SerializationTestSupport<T> : SerializationTestSupport<T, T> where T : class
{
    protected SerializationTestSupport(Type serializerType) : base(serializerType)
    {
    }
}

public abstract class SerializationTestSupport<T, TInterface> where T : class where TInterface : class
{
    protected readonly IObjectSerializer serializer;

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
    protected abstract T GetTargetObject();

    /// <summary>
    /// Verify that the target object and the object we just deserialized
    /// match.
    /// </summary>
    protected abstract void VerifyMatch(T target, T deserialized);

    /// <summary>
    /// Test that we can successfully deserialize our target
    /// class for all of the given Quartz versions.
    /// </summary>
    [Test]
    public void TestSerialization()
    {
        T targetObject = GetTargetObject();
        var data = serializer.Serialize(targetObject);
        var deserialized = serializer.DeSerialize<TInterface>(data);
        VerifyMatch(targetObject, deserialized as T);
    }

    /// <summary>
    /// Use this method in the future to generate other versions of
    /// of the serialized object file.
    /// </summary>
    [Test]
    public void WriteJobDataFile()
    {
        Assembly asm = GetType().Assembly;
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