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

using System.Reflection;

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
    public Type? ConnectionType { get; init; }

    /// <summary>
    /// Gets the type of the command.
    /// </summary>
    /// <value>The type of the command.</value>
    public Type? CommandType { get; init; }

    /// <summary>
    /// Gets the type of the parameter.
    /// </summary>
    /// <value>The type of the parameter.</value>
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
    internal PropertyInfo? ParameterDbTypeProperty
    {
        get
        {
            if (DbBinaryTypeName is null)
            {
                return null;
            }

            PropertyInfo? property = ParameterType?.GetProperty(ParameterDbTypePropertyName);
            if (property is null)
            {
                Throw.ArgumentException($"Couldn't parse parameter db type for database type '{ProductName}'");
            }

            return property;
        }
    }

    /// <summary>
    /// Gets the type of the db binary column. This is a string representation of
    /// Enum element because this information is database driver specific.
    /// </summary>
    /// <value>The type of the db binary.</value>
    public string? DbBinaryTypeName { get; init; }

    /// <summary>Gets the type of the db binary, derived from <see cref="DbBinaryTypeName" />.</summary>
    /// <value>The type of the db binary.</value>
    internal Enum? DbBinaryType
    {
        get
        {
            if (DbBinaryTypeName is null)
            {
                return null;
            }

            if (ParameterDbType is null || ParameterType is null)
            {
                Throw.ArgumentException($"Couldn't parse parameter db type for database type '{ProductName}'");
            }

            return (Enum) Enum.Parse(ParameterDbType, DbBinaryTypeName);
        }
    }

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
        _ = DbBinaryType;
        _ = ParameterDbTypeProperty;
    }
}
