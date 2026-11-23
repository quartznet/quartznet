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

namespace Quartz.EntityFrameworkCore;

/// <summary>The CLR type a column's property is given in the model.</summary>
/// <remarks>
/// Only the default value's type depends on it: EF Core refuses a default that is not of the property's type.
/// Nothing reads or writes these tables through EF Core, so the CLR type is otherwise a label.
/// </remarks>
internal enum QuartzColumnKind
{
    String,
    Int16,
    Int32,
    Int64,
    Boolean,
    Decimal,
    Binary,
}

/// <summary>One column, as the dialect's script declares it.</summary>
/// <param name="Name">The column name, as the script writes it.</param>
/// <param name="Kind">The CLR type of the column's property.</param>
/// <param name="StoreType">The store type, spelled as the model gives it to EF Core.</param>
/// <param name="Nullable">Whether the column allows <c>NULL</c>.</param>
/// <param name="DefaultValue">The default, as the CLR value <c>HasDefaultValue</c> takes.</param>
/// <param name="AddedBy">The release whose migration added the column to a table that was already there.</param>
internal sealed record QuartzColumnDefinition(
    string Name,
    QuartzColumnKind Kind,
    string StoreType,
    bool Nullable,
    object? DefaultValue = null,
    string? AddedBy = null);

/// <summary>A table's foreign key.</summary>
/// <param name="Columns">The columns in this table.</param>
/// <param name="ReferencedTable">The unprefixed name of the table referenced.</param>
/// <param name="ReferencedColumns">The columns referenced, which are that table's primary key.</param>
/// <param name="Cascade">Whether deleting the parent row deletes this one, as the dialect's script says.</param>
/// <param name="Name">
/// The script's name for the key, with the prefix as <c>{1}</c>, or <see langword="null" /> to leave it to EF Core.
/// </param>
internal sealed record QuartzForeignKeyDefinition(
    string[] Columns,
    string ReferencedTable,
    string[] ReferencedColumns,
    bool Cascade,
    string? Name = null);

/// <summary>An index.</summary>
/// <param name="Suffix">The part of the name after <c>IDX_</c> and the table prefix.</param>
/// <param name="Columns">The columns, in order.</param>
/// <param name="Descending">Each column's direction, or <see langword="null" /> when every one is ascending.</param>
/// <param name="AddedBy">The release whose migration added the index to a table that was already there.</param>
internal sealed record QuartzIndexDefinition(
    string Suffix,
    string[] Columns,
    bool[]? Descending = null,
    string? AddedBy = null);

/// <summary>A table, with everything the model says about it.</summary>
/// <param name="Name">The table name without the prefix.</param>
/// <param name="Columns">The columns, in the order the script declares them.</param>
/// <param name="PrimaryKey">The primary key's columns.</param>
/// <param name="ForeignKey">The foreign key, where the dialect's script declares one.</param>
/// <param name="Indexes">The indexes.</param>
/// <param name="AddedBy">The release whose migration created the table, or <see langword="null" /> for one 4.0 had.</param>
/// <param name="PrimaryKeyName">
/// The script's name for the primary key, with the prefix as <c>{1}</c>, or <see langword="null" /> to leave it to
/// EF Core.
/// </param>
internal sealed record QuartzTableDefinition(
    string Name,
    QuartzColumnDefinition[] Columns,
    string[] PrimaryKey,
    QuartzForeignKeyDefinition? ForeignKey,
    QuartzIndexDefinition[] Indexes,
    string? AddedBy = null,
    string? PrimaryKeyName = null);

/// <summary>A trigger the model cannot express, as a statement with the table prefix as <c>{0}</c>.</summary>
internal sealed record QuartzTriggerDefinition(string Name, string Sql);
