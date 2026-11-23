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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// Adds a dialect's table definitions to a model, as entity types without classes of their own.
/// </summary>
/// <remarks>
/// <para>
/// Each table is a shared-type entity over a property bag, so the package has no entity classes to maintain and
/// an application has none to see. EF Core's migrations compare tables, columns, keys and indexes and never
/// entity or CLR type names, which is why this shape costs an application that moves to it from another
/// package nothing.
/// </para>
/// <para>
/// Every column states its store type, nullability and default, and every index its name and directions, so
/// nothing is left to a provider's conventions. The constraint names are the scripts' on Oracle and Firebird,
/// and left to EF Core elsewhere.
/// </para>
/// </remarks>
internal static class QuartzModel
{
    /// <summary>Adds the tables.</summary>
    /// <param name="modelBuilder">The application's model.</param>
    /// <param name="dialect">The database the model is for.</param>
    /// <param name="naming">The prefix and the schema.</param>
    /// <param name="asOf">
    /// The release whose schema to add, or <see langword="null" /> for this one's. Everything a later release
    /// added is left out, which is how a test reads each release's difference.
    /// </param>
    internal static void Add(ModelBuilder modelBuilder, QuartzDialect dialect, QuartzTableNaming naming, Version? asOf = null)
    {
        List<QuartzTableDefinition> tables = [.. dialect.Tables.Where(t => Exists(t.AddedBy, asOf))];

        foreach (QuartzTableDefinition table in tables)
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(naming.EntityName(table.Name), entity =>
            {
                entity.ToTable(naming.Table(table.Name), naming.Schema);

                foreach (QuartzColumnDefinition column in table.Columns.Where(c => Exists(c.AddedBy, asOf)))
                {
                    PropertyBuilder property = Property(entity, column)
                        .HasColumnName(naming.Column(column.Name))
                        .HasColumnType(column.StoreType)
                        .IsRequired(!column.Nullable);

                    if (column.DefaultValue is not null)
                    {
                        property.HasDefaultValue(column.DefaultValue);
                    }
                }

                KeyBuilder key = entity.HasKey(table.PrimaryKey);
                if (table.PrimaryKeyName is { } primaryKeyName)
                {
                    key.HasName(naming.Constraint(primaryKeyName));
                }

                foreach (QuartzIndexDefinition index in table.Indexes.Where(i => Exists(i.AddedBy, asOf)))
                {
                    IndexBuilder<Dictionary<string, object>> built = entity
                        .HasIndex(index.Columns)
                        .HasDatabaseName(naming.Index(index.Suffix));

                    if (index.Descending is not null)
                    {
                        built.IsDescending(index.Descending);
                    }
                }
            });
        }

        // Second, so that every principal exists before a key names it.
        foreach (QuartzTableDefinition table in tables)
        {
            if (table.ForeignKey is not { } foreignKey)
            {
                continue;
            }

            ReferenceCollectionBuilder relationship = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(naming.EntityName(table.Name))
                .HasOne(naming.EntityName(foreignKey.ReferencedTable), navigationName: null)
                .WithMany()
                .HasForeignKey(foreignKey.Columns)
                .HasPrincipalKey(foreignKey.ReferencedColumns)
                .IsRequired()
                .OnDelete(foreignKey.Cascade ? DeleteBehavior.Cascade : dialect.PlainDelete);

            if (foreignKey.Name is { } foreignKeyName)
            {
                relationship.HasConstraintName(naming.Constraint(foreignKeyName));
            }
        }
    }

    /// <summary>Whether something a release added is part of the schema as of another.</summary>
    internal static bool Exists(string? addedBy, Version? asOf) =>
        addedBy is null || asOf is null || Version.Parse(addedBy) <= asOf;

    /// <summary>
    /// The column's property, with a nullable CLR type where the column is nullable: EF Core refuses to make a
    /// property of a non-nullable value type optional.
    /// </summary>
    private static PropertyBuilder Property(EntityTypeBuilder<Dictionary<string, object>> entity, QuartzColumnDefinition column) =>
        (column.Kind, column.Nullable) switch
        {
            (QuartzColumnKind.String, _) => entity.IndexerProperty<string>(column.Name),
            (QuartzColumnKind.Binary, _) => entity.IndexerProperty<byte[]>(column.Name),
            (QuartzColumnKind.Int16, false) => entity.IndexerProperty<short>(column.Name),
            (QuartzColumnKind.Int16, true) => entity.IndexerProperty<short?>(column.Name),
            (QuartzColumnKind.Int32, false) => entity.IndexerProperty<int>(column.Name),
            (QuartzColumnKind.Int32, true) => entity.IndexerProperty<int?>(column.Name),
            (QuartzColumnKind.Int64, false) => entity.IndexerProperty<long>(column.Name),
            (QuartzColumnKind.Int64, true) => entity.IndexerProperty<long?>(column.Name),
            (QuartzColumnKind.Boolean, false) => entity.IndexerProperty<bool>(column.Name),
            (QuartzColumnKind.Boolean, true) => entity.IndexerProperty<bool?>(column.Name),
            (QuartzColumnKind.Decimal, false) => entity.IndexerProperty<decimal>(column.Name),
            (QuartzColumnKind.Decimal, true) => entity.IndexerProperty<decimal?>(column.Name),
            _ => throw new InvalidOperationException($"{column.Name} has a column kind the model has no property for: {column.Kind}"),
        };
}
