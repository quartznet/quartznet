#region License
/* 
 * Copyright 2009- Marko Lahma
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
using System.Globalization;
using System.Reflection;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    /// Metadata information about specific ADO.NET driver library. Metadata is used to
    /// create correct types of object instances to interact with the underlying
    /// database.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class DbMetadata
    {
        private string parameterDbTypePropertyName;

        private string dbBinaryTypeName;
        private Enum dbBinaryType;


        /// <summary>
        /// Initializes this instance. Parses information and initializes startup
        /// values.
        /// </summary>
        public void Init()
        {
            // parse value to db binary column type
            if (dbBinaryTypeName != null)
            {
                // not inited yet
                dbBinaryType = (Enum) Enum.Parse(ParameterDbType, dbBinaryTypeName);
                ParameterDbTypeProperty = ParameterType.GetProperty(parameterDbTypePropertyName);
                if (ParameterDbTypeProperty == null)
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Couldn't parse parameter db type for database type '{0}'", ProductName));
                }
            }
        }

        /// <summary>Gets or sets the name of the assembly that holds the connection library.</summary>
        /// <value>The name of the assembly.</value>
        public virtual string AssemblyName { get; set; }

        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        /// <value>The name of the product.</value>
        public virtual string ProductName { get; set; }

        /// <summary>
        /// Gets or sets the type of the connection.
        /// </summary>
        /// <value>The type of the connection.</value>
        public virtual Type ConnectionType { get; set; }

        /// <summary>
        /// Gets or sets the type of the command.
        /// </summary>
        /// <value>The type of the command.</value>
        public virtual Type CommandType { get; set; }

        /// <summary>
        /// Gets or sets the type of the parameter.
        /// </summary>
        /// <value>The type of the parameter.</value>
        public virtual Type ParameterType { get; set; }

        /// <summary>
        /// Gets the type of the command builder.
        /// </summary>
        /// <value>The type of the command builder.</value>
        public virtual Type CommandBuilderType { get; set; }

        /// <summary>Gets the command builder's derive parameters method.</summary>
        /// <value>The command builder derive parameters method.</value>
        public virtual MethodInfo CommandBuilderDeriveParametersMethod { get; set; }

        /// <summary>
        /// Gets or sets the parameter name prefix.
        /// </summary>
        /// <value>The parameter name prefix.</value>
        public virtual string ParameterNamePrefix { get; set; }

        /// <summary>
        /// Gets or sets the type of the exception that is thrown when using driver
        /// library.
        /// </summary>
        /// <value>The type of the exception.</value>
        public virtual Type ExceptionType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether parameters are bind by name when using
        /// ADO.NET parameters.
        /// </summary>
        /// <value><c>true</c> if parameters are bind by name; otherwise, <c>false</c>.</value>
        public virtual bool BindByName { get; set; }

        /// <summary>Gets or sets the type of the database parameters.</summary>
        /// <value>The type of the parameter db.</value>
        public virtual Type ParameterDbType { get; set; }

        /// <summary>
        /// Gets the parameter db type property.
        /// </summary>
        /// <value>The parameter db type property.</value>
        public virtual PropertyInfo ParameterDbTypeProperty { get; set; }

        /// <summary>
        /// Gets the parameter is nullable property.
        /// </summary>
        /// <value>The parameter is nullable property.</value>
        public virtual PropertyInfo ParameterIsNullableProperty { get; set; }

        /// <summary>
        /// Gets or sets the type of the db binary column. This is a string representation of
        /// Enum element because this information is database driver specific.
        /// </summary>
        /// <value>The type of the db binary.</value>
        public virtual string DbBinaryTypeName
        {
            set { dbBinaryTypeName = value; }
        }

        /// <summary>Gets the type of the db binary.</summary>
        /// <value>The type of the db binary.</value>
        public virtual Enum DbBinaryType
        {
            get { return dbBinaryType; }
        }


        /// <summary>
        /// Sets the name of the parameter db type property.
        /// </summary>
        /// <value>The name of the parameter db type property.</value>
        public virtual string ParameterDbTypePropertyName
        {
            set { parameterDbTypePropertyName = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [use parameter name prefix in parameter collection].
        /// </summary>
        /// <value>
        /// 	<c>true</c> if [use parameter name prefix in parameter collection]; otherwise, <c>false</c>.
        /// </value>
        public virtual bool UseParameterNamePrefixInParameterCollection { get; set; }

        /// <summary>
        /// Gets the name of the parameter which includes the parameter prefix for this
        /// database.
        /// </summary>
        /// <param name="parameterName">Name of the parameter.</param>
        public virtual string GetParameterName(string parameterName)
        {
            return ParameterNamePrefix + parameterName; 
        }

    }
}
