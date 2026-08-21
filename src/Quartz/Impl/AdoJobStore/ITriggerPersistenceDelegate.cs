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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// An interface which provides an implementation for storing a particular
/// type of <see cref="ITrigger" />'s extended properties.
/// </summary>
/// <author>jhouse</author>
public interface ITriggerPersistenceDelegate
{
    /// <summary>
    /// Initializes the persistence delegate with the settings it works from.
    /// </summary>
    /// <remarks>
    /// Called once by the driver delegate before this delegate is used. There is no default
    /// implementation: a delegate that does not read the context has no accessor to prepare its
    /// commands with, and would fail at its first statement rather than at startup.
    /// </remarks>
    /// <param name="context">The settings the driver delegate was initialized with.</param>
    void Initialize(TriggerPersistenceDelegateContext context);

    /// <summary>
    /// Returns whether the trigger type can be handled by delegate.
    /// </summary>
    bool CanHandleTriggerType(IOperableTrigger trigger);

    /// <summary>
    /// Returns database discriminator value for trigger type.
    /// </summary>
    string GetHandledTriggerTypeDiscriminator();

    /// <summary>
    /// Inserts trigger's special properties.
    /// </summary>
    ValueTask<int> InsertExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates trigger's special properties.
    /// </summary>
    ValueTask<int> UpdateExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes trigger's special properties.
    /// </summary>
    ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads trigger's special properties.
    /// </summary>
    ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the special properties of several triggers of this delegate's type in as few round trips
    /// as the delegate can manage.
    /// </summary>
    /// <remarks>
    /// The default implementation loops the single-key overload, so a delegate that does not override
    /// this keeps working unchanged. Keys whose row is missing — the trigger was deleted concurrently,
    /// QTZ-386 — are simply absent from the result rather than failing the whole batch.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="triggerKeys">The keys of the triggers to load properties for.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask<Dictionary<TriggerKey, TriggerPropertyBundle>> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        Dictionary<TriggerKey, TriggerPropertyBundle> bundles = new(triggerKeys.Count);
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            try
            {
                bundles[triggerKey] = await LoadExtendedTriggerProperties(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // No row for this trigger: it was deleted concurrently. Leave it out of the result.
            }
        }

        return bundles;
    }

    /// <summary>
    /// Read trigger state data from open data reader.
    /// </summary>
    TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs);
}