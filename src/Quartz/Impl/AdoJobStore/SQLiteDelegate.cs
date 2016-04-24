/* 
* Copyright 2004-2009 James House 
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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is a driver delegate for the SQLiteDelegate ADO.NET driver.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class SQLiteDelegate : StdAdoDelegate
    {
        /// <summary>
        /// Gets the select next trigger to acquire SQL clause.
        /// SQLite version with LIMIT support.
        /// </summary>
        /// <returns></returns>
        protected override string GetSelectNextTriggerToAcquireSql(int maxCount)
        {
            return SqlSelectNextTriggerToAcquire + " LIMIT " + maxCount;
        }

        protected override string GetSelectNextMisfiredTriggersInStateToAcquireSql(int count)
        {
            if (count != -1)
            {
                return SqlSelectHasMisfiredTriggersInState + " LIMIT " + count;
            }
            return base.GetSelectNextMisfiredTriggersInStateToAcquireSql(count);
        }
    }
}