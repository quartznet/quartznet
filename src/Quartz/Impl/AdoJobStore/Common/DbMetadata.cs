using System;
using System.Reflection;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// Metadata information about specific ADO.NET driver library.
    /// </summary>
    public class DbMetadata
    {
        private string productName;
        private string assemblyName;
        private Type connectionType;
        private Type commandType;
        private Type parameterType;
 
        private Type parameterDbType;
        private PropertyInfo parameterDbTypeProperty;
        private PropertyInfo parameterIsNullableProperty;
        private string parameterNamePrefix;

        private Type exceptionType;
        private bool bindByName;

        private Type commandBuilderType;
        private MethodInfo commandBuilderDeriveParametersMethod;

    
        public void Init()
        {
        }

        public string AssemblyName
        {
            get { return assemblyName; }
            set { assemblyName = value; }
        }

        public string ProductName
        {
            get { return productName; }
            set { productName = value; }
        }


        public Type ConnectionType
        {
            get { return connectionType; }
            set { connectionType = value; }
        }

        public Type CommandType
        {
            get { return commandType; }
            set { commandType = value; }
        }

        public Type ParameterType
        {
            get { return parameterType; }
            set { parameterType = value; }
        }

        public Type CommandBuilderType
        {
            get { return commandBuilderType; }
        }

        public MethodInfo CommandBuilderDeriveParametersMethod
        {
            get { return commandBuilderDeriveParametersMethod; }
        }

        public string ParameterNamePrefix
        {
            get { return parameterNamePrefix; }
            set { parameterNamePrefix = value;  }
        }

        public Type ExceptionType
        {
            get { return exceptionType; }
            set { exceptionType = value; }
        }

        public bool BindByName
        {
            get { return bindByName; }
            set { bindByName = value; }
        }

        public Type ParameterDbType
        {
            get { return parameterDbType; }
            set { parameterDbType = value; }
        }

        public PropertyInfo ParameterDbTypeProperty
        {
            get { return parameterDbTypeProperty; }
        }

        public PropertyInfo ParameterIsNullableProperty
        {
            get { return parameterIsNullableProperty; }
        }

        public string GetParameterName(string parameterName)
        {
            return parameterNamePrefix + parameterName; 
        }

    }
}
