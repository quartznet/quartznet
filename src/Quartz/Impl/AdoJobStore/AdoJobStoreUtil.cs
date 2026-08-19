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

using System.Collections.Concurrent;
using System.Globalization;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This class contains utility functions for use in all delegate classes.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
internal static class AdoJobStoreUtil
{
    private static readonly ConcurrentDictionary<(string Query, string TablePrefix), string> cachedQueries = new();

    /// <summary>
    /// Substitutes the table prefix exactly as <see cref="ReplaceTablePrefix(string, string)" /> does, but
    /// remembers the result so repeated preparations of the same statement do not re-run the format scan.
    /// </summary>
    /// <remarks>
    /// The cache is unbounded, so callers must only pass statements drawn from a bounded set — in practice
    /// compile-time constants, optionally combined with one of the fixed-width key predicates. A statement
    /// built from user input would leak an entry per distinct string.
    /// </remarks>
    /// <param name="query">The unsubstituted query</param>
    /// <param name="tablePrefix">The table prefix</param>
    /// <returns>The query, with proper table prefix substituted</returns>
    public static string ReplaceTablePrefixCached(string query, string tablePrefix)
    {
        return cachedQueries.GetOrAdd(
            (query, tablePrefix),
            static key => ReplaceTablePrefix(key.Query, key.TablePrefix));
    }

    /// <summary>
    /// Replace the table prefix in a query by replacing any occurrences of
    /// "{0}" with the table prefix, and "{1}" with the unqualified portion of
    /// the table prefix (everything after the last '.', or the whole prefix when
    /// the prefix has no schema qualifier).
    /// </summary>
    /// <remarks>
    /// The "{1}" placeholder is intended for identifiers that must not include
    /// a schema, such as MySQL index names in <c>FORCE INDEX</c> hints.
    /// </remarks>
    /// <param name="query">The unsubstituted query</param>
    /// <param name="tablePrefix">The table prefix</param>
    /// <returns>The query, with proper table prefix substituted</returns>
    public static string ReplaceTablePrefix(string query, string tablePrefix)
    {
        var lastDot = tablePrefix.LastIndexOf('.');
        var unqualifiedPrefix = lastDot >= 0 ? tablePrefix.Substring(lastDot + 1) : tablePrefix;
        return string.Format(CultureInfo.InvariantCulture, query, tablePrefix, unqualifiedPrefix);
    }
}