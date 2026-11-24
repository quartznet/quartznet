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

using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// What a handler says about the change it made, for the operator's record of it: the message of the
/// Action Log entry an API request becomes, in the words the dashboard's own buttons use.
/// </summary>
/// <remarks>
/// <para>
/// Kept on the request, because the handler is what knows the reason a pause was given or how many slots
/// a backfill scheduled, and <see cref="MutationAudit" /> is what records the request once it has answered.
/// The application's audit line, <c>9007</c>, is unchanged: its template is a contract, and this is the
/// Action Log's message.
/// </para>
/// <para>
/// An answer that says nothing was applied — <c>{ "applied": false }</c> — is recorded as not done, as the
/// dashboard records a pause or an interrupt that did not land.
/// </para>
/// </remarks>
internal static class AuditNote
{
    private static readonly object NoteKey = new();
    private static readonly object IfNotAppliedKey = new();
    private static readonly object NotAppliedKey = new();

    /// <summary>
    /// Adds <paramref name="part" /> to the request's note; nothing for a blank one.
    /// </summary>
    public static void Add(HttpContext context, string? part) => Append(context, NoteKey, part);

    /// <summary>
    /// Adds <paramref name="part" /> to the note only if the answer turns out to say nothing was applied.
    /// </summary>
    public static void IfNotApplied(HttpContext context, string part) => Append(context, IfNotAppliedKey, part);

    /// <summary>
    /// Records that the answer said nothing was applied.
    /// </summary>
    public static void NotApplied(HttpContext context) => context.Items[NotAppliedKey] = true;

    /// <summary>
    /// Whether the request's answer said nothing was applied.
    /// </summary>
    public static bool WasNotApplied(HttpContext context) => context.Items.ContainsKey(NotAppliedKey);

    /// <summary>
    /// The note, its parts joined with <c>; </c> as the dashboard joins its own, or <see langword="null" />.
    /// </summary>
    public static string? Read(HttpContext context)
    {
        string? note = context.Items[NoteKey] as string;
        if (WasNotApplied(context) && context.Items[IfNotAppliedKey] is string ifNotApplied)
        {
            note = note is null ? ifNotApplied : note + "; " + ifNotApplied;
        }

        return note;
    }

    /// <summary>
    /// A pause's reason as the dashboard's Action Log writes it: <c>reason: …</c>.
    /// </summary>
    public static string? PauseReason(PauseDetails? details)
    {
        return string.IsNullOrWhiteSpace(details?.Reason) ? null : "reason: " + details.Reason.Trim();
    }

    /// <summary>
    /// An instant as the dashboard's backfill entry writes it.
    /// </summary>
    public static string Utc(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static void Append(HttpContext context, object key, string? part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        context.Items[key] = context.Items[key] is string existing ? existing + "; " + part : part;
    }
}
