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
/// Thrown when a batch of claims moved fewer rows than it held claims, and the provider's count for each
/// command does not say which.
/// </summary>
/// <remarks>
/// Nothing is wrong with the rows; the answer is only unknown. The store rolls the round back and claims
/// its triggers again one at a time, whose counts say. Not transient, so the transaction wrapper hands it
/// straight back rather than retrying the same batch.
/// </remarks>
internal sealed class ClaimOutcomeUnknownException : JobPersistenceException
{
    public ClaimOutcomeUnknownException(int moved, int claims)
        : base($"A batch of {claims} trigger claims moved {moved} row(s), and the provider did not report which; claiming them one at a time.")
    {
    }
}
