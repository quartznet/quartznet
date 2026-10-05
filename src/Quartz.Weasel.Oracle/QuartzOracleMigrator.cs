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

using Weasel.Oracle;

namespace Quartz.Weasel.Oracle;

/// <summary>
/// Weasel's Oracle migrator, refusing destructive changes.
/// </summary>
/// <remarks>
/// It used to read every introspection command's <c>LONG</c> whole as well: the commands a comparison splits
/// off dropped the fetch size, so the descending <c>PRIORITY</c> of <c>IDX_QRTZ_T_NFT_ST</c> read back as
/// Oracle's hidden <c>SYS_NC…$</c> column and every apply recreated the index. Weasel 9.37.0 keeps the fetch
/// size on a split command (JasperFx/weasel#660).
/// </remarks>
internal sealed class QuartzOracleMigrator : OracleMigrator
{
    public QuartzOracleMigrator()
    {
        RefuseDestructiveChanges = true;
    }
}
