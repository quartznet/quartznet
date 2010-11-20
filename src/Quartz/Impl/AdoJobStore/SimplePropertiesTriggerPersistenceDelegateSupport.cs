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

using Quartz.Spi;

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

        protected const string SqlSelectSimplePropsTrigger = "SELECT *" + " FROM "
                                                             + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                             + AdoConstants.ColumnTriggerName + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";

        protected const string SqlDeleteSimplePropsTrigger = "DELETE FROM "
                                                             + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                             + AdoConstants.ColumnTriggerName + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";

        protected const string SqlInsertSimplePropsTrigger = "INSERT INTO "
                                                             + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " ("
                                                             + AdoConstants.ColumnTriggerName + ", " + AdoConstants.ColumnTriggerGroup + ", "
                                                             + ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
                                                             + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
                                                             + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
                                                             + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
                                                             + ColumnBoolProp1 + ", " + ColumnBoolProp2
                                                             + ") " + " VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        protected const string SqUpdateSimplePropsTrigger = "UPDATE "
                                                            + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " SET "
                                                            + ColumnStrProp1 + " = ?, " + ColumnStrProp2 + " = ?, " + ColumnStrProp3 + " = ?, "
                                                            + ColumnIntProp1 + " = ?, " + ColumnIntProp2 + " = ?, "
                                                            + ColumnLongProp1 + " = ?, " + ColumnLongProp2 + " = ?, "
                                                            + ColumnDecProp1 + " = ?, " + ColumnDecProp2 + " = ?, "
                                                            + ColumnBoolProp1 + " = ?, " + ColumnBoolProp2
                                                            + " = ? WHERE " + AdoConstants.ColumnTriggerName
                                                            + " = ? AND " + AdoConstants.ColumnTriggerGroup + " = ?";

        protected string tablePrefix;

        public void Initialize(string tablePrefix, AdoUtil adoUtil)
        {
            this.tablePrefix = tablePrefix;
        }

        public abstract bool CanHandleTriggerType(IOperableTrigger trigger);
        public abstract string GetHandledTriggerTypeDiscriminator();

        protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

        protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            ps = conn.prepareStatement(Util.rtp(SqlDeleteSimplePropsTrigger, tablePrefix));
            ps.setString(1, triggerKey.getName());
            ps.setString(2, triggerKey.getGroup());

            return ps.executeUpdate();
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            PreparedStatement ps = null;

            ps = conn.prepareStatement(Util.rtp(SqlInsertSimplePropsTrigger, tablePrefix));
            ps.setString(1, trigger.getKey().getName());
            ps.setString(2, trigger.getKey().getGroup());
            ps.setString(3, properties.String1);
            ps.setString(4, properties.String2);
            ps.setString(5, properties.String3);
            ps.setInt(6, properties.Int1);
            ps.setInt(7, properties.Int2);
            ps.setLong(8, properties.Long1);
            ps.setLong(9, properties.Long2);
            ps.setBigDecimal(10, properties.Decimal1);
            ps.setBigDecimal(11, properties.Decimal2);
            ps.setBoolean(12, properties.Boolean1);
            ps.setBoolean(13, properties.Boolean2);

            return ps.executeUpdate();
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            PreparedStatement ps = null;
            ResultSet rs = null;


            ps = conn.prepareStatement(Util.rtp(SqlSelectSimplePropsTrigger, tablePrefix));
            ps.setString(1, triggerKey.getName());
            ps.setString(2, triggerKey.getGroup());
            rs = ps.executeQuery();

            if (rs.next())
            {
                SimplePropertiesTriggerProperties properties = new SimplePropertiesTriggerProperties();

                properties.setString1(rs.getString(ColumnStrProp1));
                properties.setString2(rs.getString(ColumnStrProp2));
                properties.setString3(rs.getString(ColumnStrProp3));
                properties.setInt1(rs.getInt(ColumnIntProp1));
                properties.setInt2(rs.getInt(ColumnIntProp2));
                properties.setLong1(rs.getInt(ColumnLongProp1));
                properties.setLong2(rs.getInt(ColumnLongProp2));
                properties.setDecimal1(rs.getBigDecimal(ColumnDecProp1));
                properties.setDecimal2(rs.getBigDecimal(ColumnDecProp2));
                properties.setBoolean1(rs.getBoolean(ColumnBoolProp1));
                properties.setBoolean2(rs.getBoolean(ColumnBoolProp2));

                return GetTriggerPropertyBundle(properties);
            }

            throw new IllegalStateException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + Util.rtp(SELECT_SIMPLE_TRIGGER, tablePrefix));
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

            PreparedStatement ps = null;

            ps = conn.prepareStatement(Util.rtp(SqUpdateSimplePropsTrigger, tablePrefix));
            ps.setString(1, properties.String1);
            ps.setString(2, properties.String2);
            ps.setString(3, properties.String3);
            ps.setInt(4, properties.Int1);
            ps.setInt(5, properties.Int2);
            ps.setLong(6, properties.Long1);
            ps.setLong(7, properties.Long2);
            ps.setBigDecimal(8, properties.Decimal1);
            ps.setBigDecimal(9, properties.Decimal2);
            ps.setBoolean(10, properties.Boolean1);
            ps.setBoolean(11, properties.Boolean2);
            ps.setString(12, trigger.getKey().getName());
            ps.setString(13, trigger.getKey().getGroup());

            return ps.executeUpdate();
        }
    }
}