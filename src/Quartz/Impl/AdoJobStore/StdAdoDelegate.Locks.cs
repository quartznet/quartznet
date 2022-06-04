using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Quartz.Impl.AdoJobStore
{
    public partial class StdAdoDelegate
    {
        public virtual string GetSelectWithLockSql()
        {
            return SqlSelectWithLock;
        }
        public virtual string GetInsertLockSql()
        {
            return SqlInsertLock;
        }
    }
}
