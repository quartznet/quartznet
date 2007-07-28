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
        private string parameterDbTypePropertyName;
        private PropertyInfo parameterIsNullableProperty;
        private string parameterNamePrefix;

        private Type exceptionType;
        private bool bindByName;

        private Type commandBuilderType;
        private MethodInfo commandBuilderDeriveParametersMethod;
        private string dbBinaryTypeName;
        private Enum dbBinaryType;


        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Init()
        {
            // parse value to db binary column type
            if (dbBinaryTypeName != null)
            {
                // not inited yet
                dbBinaryType = (Enum) Enum.Parse(parameterDbType, dbBinaryTypeName);
                parameterDbTypeProperty = parameterType.GetProperty(parameterDbTypePropertyName);
            }
        }

        /// <summary>
        /// Gets or sets the name of the assembly.
        /// </summary>
        /// <value>The name of the assembly.</value>
        public string AssemblyName
        {
            get { return assemblyName; }
            set { assemblyName = value; }
        }

        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        /// <value>The name of the product.</value>
        public string ProductName
        {
            get { return productName; }
            set { productName = value; }
        }


        /// <summary>
        /// Gets or sets the type of the connection.
        /// </summary>
        /// <value>The type of the connection.</value>
        public Type ConnectionType
        {
            get { return connectionType; }
            set { connectionType = value; }
        }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        /// <value>The type of the command.</value>
        public Type CommandType
        {
            get { return commandType; }
            set { commandType = value; }
        }

        /// <summary>
        /// Gets or sets the type of the parameter.
        /// </summary>
        /// <value>The type of the parameter.</value>
        public Type ParameterType
        {
            get { return parameterType; }
            set { parameterType = value; }
        }

        /// <summary>
        /// Gets the type of the command builder.
        /// </summary>
        /// <value>The type of the command builder.</value>
        public Type CommandBuilderType
        {
            get { return commandBuilderType; }
            set { commandBuilderType = value;  }
        }

        /// <summary>
        /// Gets the command builder derive parameters method.
        /// </summary>
        /// <value>The command builder derive parameters method.</value>
        public MethodInfo CommandBuilderDeriveParametersMethod
        {
            get { return commandBuilderDeriveParametersMethod; }
            set { commandBuilderDeriveParametersMethod = value; }
        }

        /// <summary>
        /// Gets or sets the parameter name prefix.
        /// </summary>
        /// <value>The parameter name prefix.</value>
        public string ParameterNamePrefix
        {
            get { return parameterNamePrefix; }
            set { parameterNamePrefix = value;  }
        }

        /// <summary>
        /// Gets or sets the type of the exception.
        /// </summary>
        /// <value>The type of the exception.</value>
        public Type ExceptionType
        {
            get { return exceptionType; }
            set { exceptionType = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether parameters are bind by name.
        /// </summary>
        /// <value><c>true</c> if parameters are bind by name; otherwise, <c>false</c>.</value>
        public bool BindByName
        {
            get { return bindByName; }
            set { bindByName = value; }
        }

        /// <summary>
        /// Gets or sets the type of the parameter db.
        /// </summary>
        /// <value>The type of the parameter db.</value>
        public Type ParameterDbType
        {
            get { return parameterDbType; }
            set { parameterDbType = value; }
        }

        /// <summary>
        /// Gets the parameter db type property.
        /// </summary>
        /// <value>The parameter db type property.</value>
        public PropertyInfo ParameterDbTypeProperty
        {
            get { return parameterDbTypeProperty; }
            set { parameterDbTypeProperty = value;  }
        }

        /// <summary>
        /// Gets the parameter is nullable property.
        /// </summary>
        /// <value>The parameter is nullable property.</value>
        public PropertyInfo ParameterIsNullableProperty
        {
            get { return parameterIsNullableProperty; }
            set { parameterIsNullableProperty = value; }
        }

        /// <summary>
        /// Gets or sets the type of the db binary column.
        /// </summary>
        /// <value>The type of the db binary.</value>
        public string DbBinaryTypeName
        {
            set { dbBinaryTypeName = value; }
        }

        /// <summary>
        /// Gets the type of the db binary.
        /// </summary>
        /// <value>The type of the db binary.</value>
        public Enum DbBinaryType
        {
            get { return dbBinaryType; }
        }


        /// <summary>
        /// Sets the name of the parameter db type property.
        /// </summary>
        /// <value>The name of the parameter db type property.</value>
        public string ParameterDbTypePropertyName
        {
            set { parameterDbTypePropertyName = value; }
        }

        /// <summary>
        /// Gets the name of the parameter.
        /// </summary>
        /// <param name="parameterName">Name of the parameter.</param>
        /// <returns></returns>
        public string GetParameterName(string parameterName)
        {
            return parameterNamePrefix + parameterName; 
        }

    }
}
