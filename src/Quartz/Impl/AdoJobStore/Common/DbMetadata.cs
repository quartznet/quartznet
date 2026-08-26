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

using System.Data;
using System.Data.Common;
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
/// driver. The reflection lookups that description implies — <see cref="DbBinaryTypeName" /> resolved
/// against <see cref="ParameterDbType" />, the property <see cref="ParameterDbTypePropertyName" />
/// names on <see cref="ParameterType" />, and <c>BindByName</c> on <see cref="CommandType" /> — are
/// Quartz's own, and are internal.
/// </para>
/// <para>
/// They replace the old two-phase <c>Initialize()</c> that had to be remembered. A description that
/// cannot work still fails where it is made rather than at the first binary parameter: every lookup
/// is performed once, as the metadata is registered.
/// </para>
/// <para>
/// Every <c>Type</c> here is optional, because a description only needs them when Quartz has to
/// <em>construct</em> the driver's objects — which is what <see cref="DbProvider" /> does and what a
/// trimmed application cannot rely on. A description handed to <c>ProviderFactoryDbProvider</c> or
/// <c>DataSourceDbProvider</c> gets its connections, commands and parameters from the factory or the
/// data source, so it names no type at all; what is left is the driver's parameter naming, and the two
/// typed seams below for a driver that needs a command or a binary parameter configured in a way
/// Quartz cannot name.
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
    /// Applied to every command Quartz mints for this driver, in place of the reflective
    /// <c>BindByName</c> probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quartz sets <c>BindByName</c> on the managed Oracle driver's command by looking the property up
    /// on <see cref="CommandType" />, because it cannot name <c>OracleCommand</c>. A description that
    /// names no command type — one behind a <see cref="System.Data.Common.DbProviderFactory" /> or a
    /// <see cref="System.Data.Common.DbDataSource" /> — has nothing to look the property up on, so it
    /// says what to do instead: <c>command =&gt; ((OracleCommand) command).BindByName = true</c>, in
    /// the application, which references the driver and can name the type.
    /// </para>
    /// <para>
    /// Set, it wins: the reflective probe is not attempted at all, and <see cref="BindByName" /> is left
    /// to the parameter naming it also governs.
    /// </para>
    /// </remarks>
    public Action<DbCommand>? ConfigureCommand { get; init; }

    /// <summary>
    /// Applied to a parameter carrying a binary column, in place of writing
    /// <see cref="DbBinaryTypeName" /> to the property <see cref="ParameterDbTypePropertyName" /> names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same reasoning as <see cref="ConfigureCommand" />, for the one parameter type Quartz asks for
    /// by name. It matters most on Oracle, where <see cref="System.Data.DbType.Binary" /> means
    /// <c>OracleDbType.Raw</c> and caps a job data map at two kilobytes:
    /// <c>parameter =&gt; ((OracleParameter) parameter).OracleDbType = OracleDbType.Blob</c> is what an
    /// application says instead.
    /// </para>
    /// <para>
    /// Set, it wins over the reflective setter; with neither, a binary parameter is bound as plain
    /// <see cref="System.Data.DbType.Binary" /> and the driver maps it.
    /// </para>
    /// </remarks>
    public Action<DbParameter>? ConfigureBinaryParameter { get; init; }

    /// <summary>
    /// The value a binary column's parameter is bound with. The described
    /// <see cref="DbBinaryTypeName" /> when there is one, and plain
    /// <see cref="System.Data.DbType.Binary" /> otherwise.
    /// </summary>
    /// <remarks>
    /// Never <see langword="null" />, because it is also the marker that says "this parameter carries a
    /// blob" — <c>SqlServerDelegate</c> reads it to bind <c>varbinary(max)</c> rather than let the
    /// value's length decide, and <see cref="ApplyParameterType" /> reads it to decide whether
    /// <see cref="ConfigureBinaryParameter" /> applies.
    /// </remarks>
    internal Enum BinaryParameterType => Derived.DbBinaryType ?? binaryFallback;

    /// <summary>
    /// <see cref="System.Data.DbType.Binary" />, boxed once. It is read for every blob the store writes
    /// and compared against for every parameter bound beside one, and an <see cref="Enum" /> returned
    /// from a property boxes on each read.
    /// </summary>
    private static readonly Enum binaryFallback = DbType.Binary;

    /// <summary>
    /// Applies the command settings this description carries, whichever way the command was minted.
    /// </summary>
    /// <remarks>
    /// Only <c>BindByName</c> so far, which the managed Oracle driver needs in order to bind parameters
    /// by name rather than by position. It is a property of the driver rather than of the command, so a
    /// command that came from a connection needs it set just as much as one built by reflection.
    /// </remarks>
    internal void ApplyCommandSettings(DbCommand command)
    {
        if (ConfigureCommand is { } configure)
        {
            configure(command);
            return;
        }

        Derived.BindByNameSetter?.Invoke(command, Derived.BindByNameValue);
    }

    /// <summary>
    /// Applies a provider-specific parameter type to a parameter about to be bound.
    /// </summary>
    /// <remarks>
    /// Most specific first: the typed seam for a binary parameter, then the described property on the
    /// driver's own parameter type, then the framework's own <see cref="IDataParameter.DbType" /> —
    /// which is all a description naming no types has, and is enough, because a driver that ships a
    /// <see cref="System.Data.Common.DbProviderFactory" /> maps <see cref="System.Data.DbType" /> itself.
    /// </remarks>
    internal void ApplyParameterType(IDbDataParameter parameter, Enum dataType)
    {
        if (ConfigureBinaryParameter is { } configure && Equals(dataType, BinaryParameterType))
        {
            if (parameter is not DbParameter dbParameter)
            {
                Throw.InvalidOperationException(
                    $"ConfigureBinaryParameter was given a {parameter.GetType().FullName}, which is not a DbParameter. "
                    + "The seam exists to reach a driver's own parameter type, so it is only called for parameters the driver made.");
                return;
            }

            configure(dbParameter);
            return;
        }

        if (Derived.ParameterDbTypeSetter is { } setter)
        {
            setter.Invoke(parameter, dataType);
            return;
        }

        if (dataType is DbType dbType)
        {
            parameter.DbType = dbType;
            return;
        }

        Throw.InvalidOperationException(
            $"The description of '{ProductName ?? "the driver"}' names no {nameof(ParameterType)}, so there is nowhere to "
            + $"write the parameter type '{dataType.GetType().FullName}.{dataType}'. Describe the driver's parameter type, "
            + $"or set {nameof(ConfigureBinaryParameter)}.");
    }

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
    /// <remarks>
    /// A description that names no type is not one that cannot work: it is what a driver behind a
    /// <see cref="System.Data.Common.DbProviderFactory" /> or a
    /// <see cref="System.Data.Common.DbDataSource" /> looks like, and there is nothing to derive from
    /// it. What is checked is that whatever the description <em>does</em> say hangs together — a binary
    /// type name needs a parameter type to name it on.
    /// </remarks>
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
            // Whether the driver's command supports setting BindByName directly, which the managed
            // Oracle driver needs. Skipped when the description says what to do instead, and impossible
            // when it names no command type - both of which leave the setter null and nothing to apply.
            if (metadata.ConfigureCommand is null && metadata.CommandType is not null)
            {
                PropertyInfo? bindByName = metadata.CommandType.GetProperty("BindByName", BindingFlags.Instance | BindingFlags.Public);
                if (bindByName is not null && bindByName.PropertyType == typeof(bool) && bindByName.CanWrite)
                {
                    BindByNameSetter = MethodInvoker.Create(bindByName.GetSetMethod()!);

                    // Boxed once. It never changes, and boxing it per command put an allocation under
                    // every statement the store issues on Oracle.
                    BindByNameValue = metadata.BindByName;
                }
            }

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

        public MethodInvoker? BindByNameSetter { get; }

        public object? BindByNameValue { get; }
    }
}
