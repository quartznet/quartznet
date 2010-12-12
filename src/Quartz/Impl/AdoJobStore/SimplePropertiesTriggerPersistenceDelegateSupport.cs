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

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// A base implementation of {@link TriggerPersistenceDelegate} that persists
    /// trigger fields in the "QRTZ_SIMPROP_TRIGGERS" table.  This allows extending
    /// concrete classes to simply implement a couple methods that do the work of
    /// getting/setting the trigger's fields, and creating the {@link ScheduleBuilder}
    /// for the particular type of trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <seealso cref="CalendarIntervalTriggerPersistenceDelegate" />
    /// <author>jhouse</author>
    public abstract class SimplePropertiesTriggerPersistenceDelegateSupport : ITriggerPersistenceDelegate
    {
        protected const string TableSimplePropertiesTriggers = "SIMPROP_TRIGGERS";

        protected const string ColumnStrProp1 = "STR_PROP_1";
        protected const string ColumnStrProp2 = "STR_PROP_2";
        protected const string ColumnStrProp3 = "STR_PROP_3";
        protected const string ColumnIntProp1 = "INT_PROP_1";
        protected const string ColumnIntProp2 = "INT_PROP_2";
        protected const string ColumnLongProp1 = "LONG_PROP_1";
        protected const string ColumnLongProp2 = "LONG_PROP_2";
        protected const string ColumnDecProp1 = "DEC_PROP_1";
        protected const string ColumnDecProp2 = "DEC_PROP_2";
        protected const string ColumnBoolProp1 = "BOOL_PROP_1";
        protected const string ColumnBoolProp2 = "BOOL_PROP_2";

protected static readonly string SelectSimplePropsTrigger = "SELECT *" + " FROM "
        + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
        + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + AdoConstants.ColumnTriggerName + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";

    protected static readonly string DeleteSimplePropsTrigger = "DELETE FROM "
        + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
        + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + AdoConstants.ColumnTriggerName + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";

    protected static readonly string InsertSimplePropsTrigger = "INSERT INTO "
        + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " ("
        + AdoConstants.ColumnSchedulerName + ", "
        + AdoConstants.ColumnTriggerName + ", " + AdoConstants.ColumnTriggerGroup + ", "
        + ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
        + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
        + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
        + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
        + ColumnBoolProp1 + ", " + ColumnBoolProp2 
        + ") " + " VALUES(" + StdAdoConstants.SchedulerNameSubst + ", ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

    protected static readonly string UpdateSimplePropsTrigger = "UPDATE "
        + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " SET "
        + ColumnStrProp1 + " = ?, " + ColumnStrProp2 + " = ?, " + ColumnStrProp3 + " = ?, "
        + ColumnIntProp1 + " = ?, " + ColumnIntProp2 + " = ?, "
        + ColumnLongProp1 + " = ?, " + ColumnLongProp2 + " = ?, "
        + ColumnDecProp1 + " = ?, " + ColumnDecProp2 + " = ?, "
        + ColumnBoolProp1 + " = ?, " + ColumnBoolProp2
        + " = ? WHERE " + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + AdoConstants.ColumnTriggerName
        + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";
        
        protected string tablePrefix;
        protected string schedNameLiteral;
        private AdoUtil adoUtil;

        public void Initialize(string tablePrefix, string schedName, AdoUtil adoUtil)
        {
            this.tablePrefix = tablePrefix;
            schedNameLiteral = "'" + schedName + "'";
            this.adoUtil = adoUtil;
        }

        public abstract bool CanHandleTriggerType(IOperableTrigger trigger);
        public abstract string GetHandledTriggerTypeDiscriminator();

        protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

        protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(DeleteSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(InsertSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                adoUtil.AddCommandParameter(cmd, "string1", properties.String1);
                adoUtil.AddCommandParameter(cmd, "string1", properties.String2);
                adoUtil.AddCommandParameter(cmd, "string3", properties.String3);
                adoUtil.AddCommandParameter(cmd, "int1", properties.Int1);
                adoUtil.AddCommandParameter(cmd, "int2", properties.Int2);
                adoUtil.AddCommandParameter(cmd, "long1", properties.Long1);
                adoUtil.AddCommandParameter(cmd, "long2", properties.Long2);
                adoUtil.AddCommandParameter(cmd, "decimal1", properties.Decimal1);
                adoUtil.AddCommandParameter(cmd, "decimal2", properties.Decimal2);
                adoUtil.AddCommandParameter(cmd, "boolean1", properties.Boolean1);
                adoUtil.AddCommandParameter(cmd, "boolean2", properties.Boolean2);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(SelectSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "@", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "@", triggerKey.Group);
                IDataReader rs = cmd.ExecuteReader();

                if (rs.Read())
                {
                    SimplePropertiesTriggerProperties properties = new SimplePropertiesTriggerProperties();

                    properties.String1 = (rs.GetString(ColumnStrProp1));
                    properties.String2 = (rs.GetString(ColumnStrProp2));
                    properties.String3 = (rs.GetString(ColumnStrProp3));
                    properties.Int1 = (rs.GetInt32(ColumnIntProp1));
                    properties.Int2 = (rs.GetInt32(ColumnIntProp2));
                    properties.Long1 = (rs.GetInt32(ColumnLongProp1));
                    properties.Long2 = (rs.GetInt32(ColumnLongProp2));
                    properties.Decimal1 = (rs.GetDecimal(ColumnDecProp1));
                    properties.Decimal2 = (rs.GetDecimal(ColumnDecProp2));
                    properties.Boolean1 = (rs.GetBoolean(ColumnBoolProp1));
                    properties.Boolean2 = (rs.GetBoolean(ColumnBoolProp2));

                    return GetTriggerPropertyBundle(properties);
                }
            }

            throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral));
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(UpdateSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "string1", properties.String1);
                adoUtil.AddCommandParameter(cmd, "string1", properties.String2);
                adoUtil.AddCommandParameter(cmd, "string3", properties.String3);
                adoUtil.AddCommandParameter(cmd, "int1", properties.Int1);
                adoUtil.AddCommandParameter(cmd, "int2", properties.Int2);
                adoUtil.AddCommandParameter(cmd, "long1", properties.Long1);
                adoUtil.AddCommandParameter(cmd, "long2", properties.Long2);
                adoUtil.AddCommandParameter(cmd, "decimal1", properties.Decimal1);
                adoUtil.AddCommandParameter(cmd, "decimal2", properties.Decimal2);
                adoUtil.AddCommandParameter(cmd, "boolean1", properties.Boolean1);
                adoUtil.AddCommandParameter(cmd, "boolean2", properties.Boolean2);
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}