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

namespace Quartz;

/// <summary>
/// What the cluster makes of one node's check-in history.
/// </summary>
/// <remarks>
/// The three values are read off one clock — the observing node's — against the check-in stamps every
/// node writes, so this is what <em>this</em> node believes about the others. The
/// <see cref="Failed" /> boundary is the same one the recovery sweep applies, so a node this listing
/// calls failed is a node whose work the cluster is about to take over.
/// </remarks>
public enum ClusterNodeState
{
    /// <summary>
    /// The node checked in within its own check-in interval: it is doing what a running node does.
    /// </summary>
    Alive,

    /// <summary>
    /// The node has missed a check-in but has not yet been silent long enough to be declared dead.
    /// </summary>
    /// <remarks>
    /// Normal under load or across a slow link, and the state a node passes through on its way to
    /// <see cref="Failed" />. Nothing is recovered from an overdue node.
    /// </remarks>
    Overdue,

    /// <summary>
    /// The node has been silent past the point at which the cluster stops waiting for it.
    /// </summary>
    /// <remarks>
    /// The next check-in pass by any node recovers this one's in-flight work and deletes its row, so a
    /// node reported failed is normally reported once and then disappears from the listing.
    /// </remarks>
    Failed
}
