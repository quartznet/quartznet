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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// One row of QRTZ_SIMPROP_TRIGGERS: the generic column set a trigger's own properties are stored in.
/// </summary>
/// <remarks>
/// <para>
/// This is the payload of the <see cref="SimplePropertiesTriggerPersistenceDelegateBase" /> seam. A
/// delegate written against that base decides what each column means for its trigger family —
/// <see cref="CalendarIntervalTriggerPersistenceDelegate" /> puts the repeat interval in
/// <see cref="Int1" /> and its unit in <see cref="String1" />, for instance — and the base does the
/// reading and writing. The columns are deliberately anonymous: the schema is fixed, and a family that
/// needs a fourth string is out of luck rather than adding a column.
/// </para>
/// <para>
/// It is built in one go and read afterwards. A delegate returns a fully described row from
/// <see cref="SimplePropertiesTriggerPersistenceDelegateBase.GetTriggerProperties" />, and is handed a
/// fully read one in <see cref="SimplePropertiesTriggerPersistenceDelegateBase.GetTriggerPropertyBundle" />;
/// neither side edits what the other made.
/// </para>
/// </remarks>
public sealed record SimplePropertiesTriggerProperties
{
    /// <summary>The STR_PROP_1 column.</summary>
    public string? String1 { get; init; }

    /// <summary>The STR_PROP_2 column.</summary>
    public string? String2 { get; init; }

    /// <summary>The STR_PROP_3 column.</summary>
    public string? String3 { get; init; }

    /// <summary>The INT_PROP_1 column.</summary>
    public int Int1 { get; init; }

    /// <summary>The INT_PROP_2 column.</summary>
    public int Int2 { get; init; }

    /// <summary>The LONG_PROP_1 column.</summary>
    public long Long1 { get; init; }

    /// <summary>The LONG_PROP_2 column.</summary>
    public long Long2 { get; init; }

    /// <summary>The DEC_PROP_1 column.</summary>
    public decimal Decimal1 { get; init; }

    /// <summary>The DEC_PROP_2 column.</summary>
    public decimal Decimal2 { get; init; }

    /// <summary>The BOOL_PROP_1 column, read and written through the dialect's boolean conversion.</summary>
    public bool Boolean1 { get; init; }

    /// <summary>The BOOL_PROP_2 column, read and written through the dialect's boolean conversion.</summary>
    public bool Boolean2 { get; init; }

    /// <summary>
    /// The TIME_ZONE_ID column, which got a column of its own in 2.6. A delegate reading a row written
    /// before that finds the id in <see cref="String2" /> instead.
    /// </summary>
    public string? TimeZoneId { get; init; }
}
