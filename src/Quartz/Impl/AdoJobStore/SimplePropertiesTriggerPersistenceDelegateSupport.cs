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
/**
 * A base implementation of {@link TriggerPersistenceDelegate} that persists 
 * trigger fields in the "QRTZ_SIMPROP_TRIGGERS" table.  This allows extending
 * concrete classes to simply implement a couple methods that do the work of
 * getting/setting the trigger's fields, and creating the {@link ScheduleBuilder}
 * for the particular type of trigger. 
 * 
 * @see CalendarIntervalTriggerPersistenceDelegate for an example extension
 * 
 * @author jhouse
 */

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

protected static readonly string SELECT_SIMPLE_PROPS_TRIGGER = "SELECT *" + " FROM "
        + StdAdoConstants.TablePrefixSubst + TABLE_SIMPLE_PROPERTIES_TRIGGERS + " WHERE "
        + StdAdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + COL_TRIGGER_NAME + " = ? AND " + COL_TRIGGER_GROUP + " = ?";

    protected static readonly string DELETE_SIMPLE_PROPS_TRIGGER = "DELETE FROM "
        + StdAdoConstants.TablePrefixSubst + TABLE_SIMPLE_PROPERTIES_TRIGGERS + " WHERE "
        + StdAdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + COL_TRIGGER_NAME + " = ? AND " + COL_TRIGGER_GROUP + " = ?";

    protected static readonly string INSERT_SIMPLE_PROPS_TRIGGER = "INSERT INTO "
        + StdAdoConstants.TablePrefixSubst + TABLE_SIMPLE_PROPERTIES_TRIGGERS + " ("
        + StdAdoConstants.ColumnSchedulerName + ", "
        + COL_TRIGGER_NAME + ", " + COL_TRIGGER_GROUP + ", "
        + COL_STR_PROP_1 + ", " + COL_STR_PROP_2 + ", " + COL_STR_PROP_3 + ", "
        + COL_INT_PROP_1 + ", " + COL_INT_PROP_2 + ", "
        + COL_LONG_PROP_1 + ", " + COL_LONG_PROP_2 + ", "
        + COL_DEC_PROP_1 + ", " + COL_DEC_PROP_2 + ", "
        + COL_BOOL_PROP_1 + ", " + COL_BOOL_PROP_2 
        + ") " + " VALUES(" + StdAdoConstants.SchedulerNameSubst + ", ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

    protected static readonly string UPDATE_SIMPLE_PROPS_TRIGGER = "UPDATE "
        + StdAdoConstants.TablePrefixSubst + TABLE_SIMPLE_PROPERTIES_TRIGGERS + " SET "
        + COL_STR_PROP_1 + " = ?, " + COL_STR_PROP_2 + " = ?, " + COL_STR_PROP_3 + " = ?, "
        + COL_INT_PROP_1 + " = ?, " + COL_INT_PROP_2 + " = ?, "
        + COL_LONG_PROP_1 + " = ?, " + COL_LONG_PROP_2 + " = ?, "
        + COL_DEC_PROP_1 + " = ?, " + COL_DEC_PROP_2 + " = ?, "
        + COL_BOOL_PROP_1 + " = ?, " + COL_BOOL_PROP_2 
        + " = ? WHERE " + StdAdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
        + " AND " + COL_TRIGGER_NAME
        + " = ? AND " + COL_TRIGGER_GROUP + " = ?";
        
        protected string tablePrefix;
        protected string schedNameLiteral;
        private AdoUtil adoUtil;

        public void Initialize(string tablePrefix, string schedName, AdoUtil adoUtil)
        {
            this.tablePrefix = tablePrefix;
            this.schedNameLiteral = "'" + schedName + "'";
            this.adoUtil = adoUtil;
        }

        public abstract bool CanHandleTriggerType(IOperableTrigger trigger);
        public abstract string GetHandledTriggerTypeDiscriminator();

        protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

        protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = adoUtil.PrepareCommand(AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "@", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "@", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = adoUtil.PrepareCommand(AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "@", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "@", trigger.Key.Group);
                adoUtil.AddCommandParameter(cmd, "@", properties.String1);
                adoUtil.AddCommandParameter(cmd, "@", properties.String2);
                adoUtil.AddCommandParameter(cmd, "@", properties.String3);
                adoUtil.AddCommandParameter(cmd, "@", properties.Int1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Int2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Long1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Long2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Decimal1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Decimal2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Boolean1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Boolean2);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = adoUtil.PrepareCommand(AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimplePropsTrigger, tablePrefix, schedNameLiteral)))
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

            throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + Util.rtp(SELECT_SIMPLE_TRIGGER, tablePrefix));
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = adoUtil.PrepareCommand(AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqUpdateSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "@", properties.String1);
                adoUtil.AddCommandParameter(cmd, "@", properties.String2);
                adoUtil.AddCommandParameter(cmd, "@", properties.String3);
                adoUtil.AddCommandParameter(cmd, "@", properties.Int1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Int2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Long1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Long2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Decimal1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Decimal2);
                adoUtil.AddCommandParameter(cmd, "@", properties.Boolean1);
                adoUtil.AddCommandParameter(cmd, "@", properties.Boolean2);
                adoUtil.AddCommandParameter(cmd, "@", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "@", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}