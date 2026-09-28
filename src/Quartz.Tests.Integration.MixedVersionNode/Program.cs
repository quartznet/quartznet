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

using Quartz.Diagnostics;
using Quartz.Tests.Integration.MixedVersionNode;

NodeOptions options;
try
{
    options = NodeOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(NodeOptions.Usage);
    return 2;
}

// Warnings and errors only, to standard error: standard output is the protocol.
LogProvider.SetLogProvider(new StandardErrorLoggerFactory(options.InstanceId, options.LogLevel));

Node node;
try
{
    node = await Node.Create(options).ConfigureAwait(false);
}
catch (Exception exception)
{
    Protocol.Reply(Protocol.Error, exception);
    return 3;
}

Protocol.Reply(Protocol.Ready, node.Describe());

return await node.Serve(Console.In).ConfigureAwait(false);
