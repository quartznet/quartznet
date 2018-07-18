using System;

using Quartz.Impl.Matchers;
using Quartz.Util;

using Raven.Client.Documents.Linq;

namespace Quartz.Impl.RavenDB
{
    internal static class RavenExtensions
    {
        private const char DocumentIdPartSeparator = '/';
        private static  readonly char[] DocumentIdPartSeparatorArray = new [] {DocumentIdPartSeparator};

        internal static void Validate<T>(this Key<T> key)
        {
            if (key.Group.IndexOf(DocumentIdPartSeparator) > -1 || key.Name.IndexOf(DocumentIdPartSeparator) > -1)
            {
                throw new ArgumentException("trigger or job keys cannot contain '/' character");
            }
        }

        internal static string DocumentId<T>(this Key<T> key, string schedulerName)
            => schedulerName + DocumentIdPartSeparator + key.Group + DocumentIdPartSeparator + key.Name;

        internal static JobKey JobKeyFromDocumentId(this string id)
        {
            var parts = id.Split(DocumentIdPartSeparatorArray);
            return new JobKey(parts[2], parts[1]);
        }

        internal static TriggerKey TriggerIdFromDocumentId(this string id)
        {
            var parts = id.Split(DocumentIdPartSeparatorArray);
            return new TriggerKey(parts[2], parts[1]);
        }

        internal static IRavenQueryable<T> WhereMatches<T, TKey>(
            this IRavenQueryable<T> queryable,
            GroupMatcher<TKey> matcher)
            where T : IHasGroup
            where TKey : Key<TKey>
        {
            if (matcher.CompareWithOperator.Equals(StringOperator.Contains))
            {
                return queryable.Where(x => x.Group.Contains(matcher.CompareToValue));
            }

            if (matcher.CompareWithOperator.Equals(StringOperator.StartsWith))
            {
                return queryable.Where(x => x.Group.StartsWith(matcher.CompareToValue));
            }

            if (matcher.CompareWithOperator.Equals(StringOperator.EndsWith))
            {
                return queryable.Where(x => x.Group.EndsWith(matcher.CompareToValue));
            }

            if (matcher.CompareWithOperator.Equals(StringOperator.Equality))
            {
                return queryable.Where(x => x.Group == matcher.CompareToValue);
            }

            if (matcher.CompareWithOperator.Equals(StringOperator.Anything))
            {
                return queryable;
            }

            throw new NotSupportedException();
        }
    }
}