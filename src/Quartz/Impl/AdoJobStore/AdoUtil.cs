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
using System.Data;
using System.Data.Common;
using System.Text;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Common helper methods for working with ADO.NET.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class AdoUtil
    {
        private static readonly ILog log = LogProvider.GetLogger("Quartz.SQL");
        private readonly IDbProvider dbProvider;

        public AdoUtil(IDbProvider dbProvider)
        {
            this.dbProvider = dbProvider;
        }

        public void AddCommandParameter(IDbCommand cmd, string paramName, object paramValue)
        {
            AddCommandParameter(cmd, paramName, paramValue, null, null);
        }

        public void AddCommandParameter(
            IDbCommand cmd,
            string paramName,
            object paramValue,
            Enum dataType,
            int? size)
        {

            IDbDataParameter param = cmd.CreateParameter();
            if (dataType != null)
            {
                SetDataTypeToCommandParameter(param, dataType);
            }

            if (size != null)
            {
                param.Size = size.Value;
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
            dbProvider.Metadata.ParameterDbTypeProperty.SetMethod.Invoke(param, new[] { parameterType });
        }

        public struct DbCommandParameterValue
        {
            public object Value;
            public Enum DataType;
            public int? Size;
        }

        public DbCommandParameterValue CreateParamValue(object value, Enum dataType = null, int? size = null)
        {
            return new DbCommandParameterValue { Value = value, DataType = dataType, Size = size };
        }

        public DbCommand PrepareCommandBatchByTemplateCloning(
            ConnectionAndTransactionHolder cth, 
            string commandTemplate, 
            string[] paramNames, 
            DbCommandParameterValue[][] paramValuesBatch,
            bool forSelect = false)
        {
            DbCommand cmd = PrepareCommand(cth);
            var batchText = new StringBuilder();

            for (int i = 0; i < paramValuesBatch.GetLength(0); i++)
            {
                var paramValues = paramValuesBatch[i];
                if (i > 0 && forSelect)
                {
                    batchText.AppendLine("UNION ALL");
                }

                var statement = commandTemplate;
                for (int j = 0; j < paramNames.Length; j++)
                {
                    statement = statement.Replace($"@{paramNames[j]}", $"@{paramNames[j]}{i}pp");
                }

                if (forSelect)
                {
                    batchText.AppendLine(statement);
                }
                else
                {
                    batchText.Append(statement).AppendLine(";");
                }

                for (int j = 0; j < paramNames.Length; j++)
                {
                    AddCommandParameter(cmd, $"{paramNames[j]}{i}pp", paramValues[j].Value, paramValues[j].DataType, paramValues[j].Size);
                }
            }
            cmd.CommandText = batchText.ToString();

            return cmd;
        }

        public DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText = null)
        {
            DbCommand cmd = dbProvider.CreateCommand();
            if (!string.IsNullOrEmpty(commandText))
            {
                cmd.CommandText = commandText;
            }

            cth.Attach(cmd);

            if (log.IsDebugEnabled() && !string.IsNullOrEmpty(commandText))
            {
                log.DebugFormat("Prepared SQL: {0}", cmd.CommandText);
            }

            return cmd;
        }
    }
}
