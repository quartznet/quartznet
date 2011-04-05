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
    /// getting/setting the trigger's fields, and creating the <see cref="IScheduleBuilder" />
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

        protected const string SelectSimplePropsTrigger = "SELECT *" + " FROM "
                                                            + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                            + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
                                                            + " AND " + AdoConstants.ColumnTriggerName + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup";

        protected const string DeleteSimplePropsTrigger = "DELETE FROM "
                                                            + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                            + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
                                                            + " AND " + AdoConstants.ColumnTriggerName + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup";

        protected const string InsertSimplePropsTrigger = "INSERT INTO "
                                                            + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " ("
                                                            + AdoConstants.ColumnSchedulerName + ", "
                                                            + AdoConstants.ColumnTriggerName + ", " + AdoConstants.ColumnTriggerGroup + ", "
                                                            + ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
                                                            + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
                                                            + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
                                                            + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
                                                            + ColumnBoolProp1 + ", " + ColumnBoolProp2
                                                            + ") " + " VALUES(" + StdAdoConstants.SchedulerNameSubst + ", @triggerName, @triggerGroup, @string1, @string2, @string3, @int1, @int2, @long1, @long2, @decimal1, @decimal2, @boolean1, @boolean2)";

        protected const string UpdateSimplePropsTrigger = "UPDATE "
                                                            + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " SET "
                                                            + ColumnStrProp1 + " = @string1, " + ColumnStrProp2 + " = @string2, " + ColumnStrProp3 + " = @string3, "
                                                            + ColumnIntProp1 + " = @int1, " + ColumnIntProp2 + " = @int2, "
                                                            + ColumnLongProp1 + " = @long1, " + ColumnLongProp2 + " = @long2, "
                                                            + ColumnDecProp1 + " = @decimal1, " + ColumnDecProp2 + " = @decimal2, "
                                                            + ColumnBoolProp1 + " = @boolean1, " + ColumnBoolProp2
                                                            + " = @boolean2 WHERE " + AdoConstants.ColumnSchedulerName + " = " + StdAdoConstants.SchedulerNameSubst
                                                            + " AND " + AdoConstants.ColumnTriggerName
                                                            + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup"; 

        protected string tablePrefix;
        protected string schedNameLiteral;
        protected ICommandAccessor commandAccessor;

        public void Initialize(string tablePrefix, string schedName, ICommandAccessor commandAccessor)
        {
            this.tablePrefix = tablePrefix;
            this.commandAccessor = commandAccessor;
            schedNameLiteral = "'" + schedName + "'";
        }

        public abstract bool CanHandleTriggerType(IOperableTrigger trigger);
        public abstract string GetHandledTriggerTypeDiscriminator();

        protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

        protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(DeleteSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(InsertSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                commandAccessor.AddCommandParameter(cmd, "string1", properties.String1);
                commandAccessor.AddCommandParameter(cmd, "string2", properties.String2);
                commandAccessor.AddCommandParameter(cmd, "string3", properties.String3);
                commandAccessor.AddCommandParameter(cmd, "int1", properties.Int1);
                commandAccessor.AddCommandParameter(cmd, "int2", properties.Int2);
                commandAccessor.AddCommandParameter(cmd, "long1", properties.Long1);
                commandAccessor.AddCommandParameter(cmd, "long2", properties.Long2);
                commandAccessor.AddCommandParameter(cmd, "decimal1", properties.Decimal1);
                commandAccessor.AddCommandParameter(cmd, "decimal2", properties.Decimal2);
                commandAccessor.AddCommandParameter(cmd, "boolean1", properties.Boolean1);
                commandAccessor.AddCommandParameter(cmd, "boolean2", properties.Boolean2);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(SelectSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                using (IDataReader rs = cmd.ExecuteReader())
                {
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
            }

            throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral));
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(UpdateSimplePropsTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "string1", properties.String1);
                commandAccessor.AddCommandParameter(cmd, "string2", properties.String2);
                commandAccessor.AddCommandParameter(cmd, "string3", properties.String3);
                commandAccessor.AddCommandParameter(cmd, "int1", properties.Int1);
                commandAccessor.AddCommandParameter(cmd, "int2", properties.Int2);
                commandAccessor.AddCommandParameter(cmd, "long1", properties.Long1);
                commandAccessor.AddCommandParameter(cmd, "long2", properties.Long2);
                commandAccessor.AddCommandParameter(cmd, "decimal1", properties.Decimal1);
                commandAccessor.AddCommandParameter(cmd, "decimal2", properties.Decimal2);
                commandAccessor.AddCommandParameter(cmd, "boolean1", properties.Boolean1);
                commandAccessor.AddCommandParameter(cmd, "boolean2", properties.Boolean2);
                commandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}