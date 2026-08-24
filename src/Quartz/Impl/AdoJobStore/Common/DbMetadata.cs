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

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Metadata information about specific ADO.NET driver library. Metadata is used to
/// create correct types of object instances to interact with the underlying
/// database.
/// </summary>
/// <remarks>
/// <para>
/// An init-only record: a description is built once — with an object initializer, or copied with a
/// <c>with</c> expression — and cannot drift afterwards. Everything on it is something you say about a
/// driver. The two reflection lookups that description implies — <see cref="DbBinaryTypeName" />
/// resolved against <see cref="ParameterDbType" />, and the property
/// <see cref="ParameterDbTypePropertyName" /> names on <see cref="ParameterType" /> — are Quartz's
/// own, and are internal.
/// </para>
/// <para>
/// They replace the old two-phase <c>Initialize()</c> that had to be remembered. A description that
/// cannot work still fails where it is made rather than at the first binary parameter: both lookups
/// are performed once, as the metadata is registered.
/// </para>
/// </remarks>
/// <author>Marko Lahma</author>
public sealed record DbMetadata
{
    /// <summary>Gets the name of the assembly that holds the connection library.</summary>
    /// <value>The name of the assembly.</value>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// Gets the name of the product.
    /// </summary>
    /// <value>The name of the product.</value>
    public string? ProductName { get; init; }

    /// <summary>
    /// Gets the type of the connection.
    /// </summary>
    /// <value>The type of the connection.</value>
    /// <remarks>
    /// <see cref="DbProvider" /> constructs one of these per connection it opens, so the type has to
    /// keep its public constructors through trimming.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type? ConnectionType { get; init; }

    /// <summary>
    /// Gets the type of the command.
    /// </summary>
    /// <value>The type of the command.</value>
    /// <remarks>
    /// <see cref="DbProvider" /> constructs one of these per command it runs and, for a driver that
    /// binds parameters by name, sets <c>BindByName</c> on it.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type? CommandType { get; init; }

    /// <summary>
    /// Gets the type of the parameter.
    /// </summary>
    /// <value>The type of the parameter.</value>
    /// <remarks>
    /// The property <see cref="ParameterDbTypePropertyName" /> names is looked up on this type.
    /// </remarks>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type? ParameterType { get; init; }

    /// <summary>
    /// Gets the parameter name prefix.
    /// </summary>
    /// <value>The parameter name prefix.</value>
    public string? ParameterNamePrefix { get; init; }

    /// <summary>
    /// Gets the type of the exception that is thrown when using driver
    /// library.
    /// </summary>
    /// <value>The type of the exception.</value>
    public Type? ExceptionType { get; init; }

    /// <summary>
    /// Gets a value indicating whether parameters are bind by name when using
    /// ADO.NET parameters.
    /// </summary>
    /// <value><c>true</c> if parameters are bind by name; otherwise, <c>false</c>.</value>
    public bool BindByName { get; init; }

    /// <summary>Gets the type of the database parameters.</summary>
    /// <value>The type of the parameter db.</value>
    public Type? ParameterDbType { get; init; }

    /// <summary>
    /// Gets the property on <see cref="ParameterType" /> named by
    /// <see cref="ParameterDbTypePropertyName" />, derived from the described values when
    /// <see cref="DbBinaryTypeName" /> is set.
    /// </summary>
    /// <value>The parameter db type property.</value>
    internal PropertyInfo? ParameterDbTypeProperty => Derived.ParameterDbTypeProperty;

    /// <summary>
    /// Setter for <see cref="ParameterDbTypeProperty" />, prepared once so that binding a binary
    /// parameter does not go back through <see cref="MethodBase.Invoke(object, object[])" /> and its
    /// argument array every time.
    /// </summary>
    internal MethodInvoker? ParameterDbTypeSetter => Derived.ParameterDbTypeSetter;

    /// <summary>
    /// Gets the type of the db binary column. This is a string representation of
    /// Enum element because this information is database driver specific.
    /// </summary>
    /// <value>The type of the db binary.</value>
    public string? DbBinaryTypeName { get; init; }

    /// <summary>Gets the type of the db binary, derived from <see cref="DbBinaryTypeName" />.</summary>
    /// <value>The type of the db binary.</value>
    internal Enum? DbBinaryType => Derived.DbBinaryType;

    /// <summary>
    /// Gets the name of the parameter db type property.
    /// </summary>
    /// <value>The name of the parameter db type property.</value>
    public string ParameterDbTypePropertyName { get; init; } = null!;

    /// <summary>
    /// Gets a value indicating whether [use parameter name prefix in parameter collection].
    /// </summary>
    /// <value>
    /// 	<c>true</c> if [use parameter name prefix in parameter collection]; otherwise, <c>false</c>.
    /// </value>
    public bool UseParameterNamePrefixInParameterCollection { get; init; }

    /// <summary>
    /// Gets the name of the parameter which includes the parameter prefix for this
    /// database.
    /// </summary>
    /// <param name="parameterName">Name of the parameter.</param>
    public string GetParameterName(string parameterName)
    {
        return ParameterNamePrefix + parameterName;
    }

    /// <summary>
    /// Derives the computed members once so that a description that cannot work fails where the
    /// description is made rather than when the first binary parameter is bound.
    /// </summary>
    internal void Validate()
    {
        _ = Derived;
    }

    /// <summary>
    /// The members derived from this description by reflection, worked out once per description.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept beside the record rather than in fields on it. A record's generated equality compares every
    /// instance field, so a memoization field would make two descriptions that say the same thing about
    /// a driver compare unequal as soon as one of them had been used. The description stays what it
    /// always was — a value — and what reflection makes of it hangs off it here.
    /// </para>
    /// <para>
    /// <see cref="DbBinaryType" /> used to run <see cref="Enum.Parse(Type, string)" /> on every read and
    /// <see cref="ParameterDbTypeProperty" /> a <see cref="Type.GetProperty(string)" />, and the write
    /// path reads them once per binary column.
    /// </para>
    /// </remarks>
    private DerivedMetadata Derived => derived.GetValue(this, static metadata => new DerivedMetadata(metadata));

    private static readonly ConditionalWeakTable<DbMetadata, DerivedMetadata> derived = new();

    private sealed class DerivedMetadata
    {
        public DerivedMetadata(DbMetadata metadata)
        {
            if (metadata.DbBinaryTypeName is null)
            {
                return;
            }

            if (metadata.ParameterDbType is null || metadata.ParameterType is null)
            {
                Throw.ArgumentException($"Couldn't parse parameter db type for database type '{metadata.ProductName}'");
            }

            DbBinaryType = (Enum) Enum.Parse(metadata.ParameterDbType, metadata.DbBinaryTypeName);

            PropertyInfo? property = metadata.ParameterType.GetProperty(metadata.ParameterDbTypePropertyName);
            if (property?.SetMethod is null)
            {
                Throw.ArgumentException($"Couldn't parse parameter db type for database type '{metadata.ProductName}'");
            }

            ParameterDbTypeProperty = property;
            ParameterDbTypeSetter = MethodInvoker.Create(property.SetMethod);
        }

        public Enum? DbBinaryType { get; }

        public PropertyInfo? ParameterDbTypeProperty { get; }

        public MethodInvoker? ParameterDbTypeSetter { get; }
    }
}
