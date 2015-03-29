using Quartz.Impl.Matchers;
using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class GroupMatcherDto
    {
        public string NameContains { get; set; }
        public string NameEndsWith { get; set; }
        public string NameStartsWith { get; set; }
        public string NameEquals { get; set; }

        public GroupMatcher<TriggerKey> GetTriggerGroupMatcher()
        {
            return GetGroupMatcher<TriggerKey>();
        }

        public GroupMatcher<JobKey> GetJobGroupMatcher()
        {
            return GetGroupMatcher<JobKey>();
        }

        private GroupMatcher<T> GetGroupMatcher<T>() where T : Key<T>
        {
            if (!string.IsNullOrWhiteSpace(NameContains))
            {
                return GroupMatcher<T>.GroupContains(NameContains);
            }
            if (!string.IsNullOrWhiteSpace(NameStartsWith))
            {
                return GroupMatcher<T>.GroupStartsWith(NameStartsWith);
            }
            if (!string.IsNullOrWhiteSpace(NameEndsWith))
            {
                return GroupMatcher<T>.GroupEndsWith(NameEndsWith);
            }
            if (!string.IsNullOrWhiteSpace(NameEquals))
            {
                return GroupMatcher<T>.GroupEquals(NameEquals);
            }
            return GroupMatcher<T>.AnyGroup();
        }
    }
}