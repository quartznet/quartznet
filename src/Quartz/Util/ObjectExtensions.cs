using System;
using System.Reflection;

namespace Quartz.Util
{
    /// <summary>
    /// Generic extension methods for objects.
    /// </summary>
    public static class ObjectExtensions
    {
        public static string AssemblyQualifiedNameWithoutVersion(this Type type)
        {
            string retValue = type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;
            return retValue;
        }
    }
}