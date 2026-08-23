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

using System.Data.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// A statement and the parameters bound to it, kept as data rather than issued, so that the same
/// definition can go out either as a standalone command or as one command of a <see cref="DbBatch" />.
/// </summary>
/// <remarks>
/// The text is what would otherwise have been handed to <see cref="IDbAccessor.PrepareCommand" />:
/// the table prefix already substituted, and the parameters still spelled with <c>@</c>. A driver
/// that spells them some other way has its spelling applied when the statement is issued, which is
/// the only point at which the driver is known.
/// </remarks>
/// <param name="Sql">The statement text.</param>
/// <param name="Parameters">
/// The parameters to bind, in the order the statement mentions them — which is what a driver that
/// binds positionally rather than by name needs.
/// </param>
public readonly record struct SqlStatement(string Sql, List<SqlStatementParameter> Parameters);

/// <summary>
/// One parameter of a <see cref="SqlStatement" />.
/// </summary>
/// <param name="Name">Name of the parameter, without the driver's prefix.</param>
/// <param name="Value">The value to bind. <see langword="null" /> binds as <see cref="DBNull" />.</param>
/// <param name="DataType">
/// Provider-specific parameter type, for the columns whose type cannot be inferred from the value —
/// a binary column being the one this store needs.
/// </param>
public readonly record struct SqlStatementParameter(string Name, object? Value, Enum? DataType = null);
