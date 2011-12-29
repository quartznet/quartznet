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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is a driver delegate for the Oracle database.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class OracleDelegate : StdAdoDelegate
    {
        /// <summary>
        /// Creates the SQL for select next trigger to acquire.
        /// </summary>
        protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
        {
            string sqlSelectNextTriggerToAcquire = SqlSelectNextTriggerToAcquire;

            int whereEndIdx = sqlSelectNextTriggerToAcquire.IndexOf("WHERE") + 6;
            string beginningAndWhere = sqlSelectNextTriggerToAcquire.Substring(0, whereEndIdx);
            string theRest = sqlSelectNextTriggerToAcquire.Substring(whereEndIdx);

            // add limit clause to correct place
            return beginningAndWhere + " rownum <= " + maxCount + " AND " + theRest;
        }

        /// <summary>
        /// Gets the db presentation for boolean value. For Oracle we use true/false of "1"/"0".
        /// </summary>
        /// <param name="booleanValue">Value to map to database.</param>
        /// <returns></returns>
        public override object GetDbBooleanValue(bool booleanValue)
        {
            return booleanValue ? "1" : "0";
        }

        public override bool GetBooleanFromDbValue(object columnValue)
        {
            // we store things as string in oracle with 1/0 as value
            if (columnValue != null && columnValue != DBNull.Value)
            {
                return Convert.ToInt32(columnValue) == 1;
            }

            throw new ArgumentException("Value must be non-null.");
        }
    }
}