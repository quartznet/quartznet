#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Data;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Common helper methods for working with ADO.NET.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class AdoUtil
    {
        private readonly IDbProvider dbProvider;

        public AdoUtil(IDbProvider dbProvider)
        {
            this.dbProvider = dbProvider;
        }

        public void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue)
        {
            AddCommandParameter(cmd, paramName, paramValue, null);
        }

        public void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue, Enum dataType)
        {
            IDbDataParameter param = cmd.CreateParameter();
            if (dataType != null)
            {
                SetDataTypeToCommandParameter(param, dataType);
            }
            param.ParameterName = dbProvider.Metadata.GetParameterName(paramName);
            param.Value = paramValue ?? DBNull.Value;
            cmd.Parameters.Add(param);

            if (!dbProvider.Metadata.BindByName)
            {
                cmd.CommandText = cmd.CommandText.Replace("@" + paramName, dbProvider.Metadata.ParameterNamePrefix);
            }
            else if (dbProvider.Metadata.ParameterNamePrefix != "@")
            {
                // we need to replace
                cmd.CommandText = cmd.CommandText.Replace("@" + paramName, dbProvider.Metadata.ParameterNamePrefix + paramName);
            }
        }

        private void SetDataTypeToCommandParameter(IDbDataParameter param, object parameterType)
        {
            dbProvider.Metadata.ParameterDbTypeProperty.GetSetMethod().Invoke(param, new object[] { parameterType });
        }

        public IDbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            IDbCommand cmd = dbProvider.CreateCommand();
            cmd.CommandText = commandText;
            cmd.Connection = cth.Connection;
            cmd.Transaction = cth.Transaction;
            return cmd;
        }
    }
}
