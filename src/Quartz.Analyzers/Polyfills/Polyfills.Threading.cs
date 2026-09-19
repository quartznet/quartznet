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

namespace System.Threading;

/// <summary>
/// <see cref="Lock" />, which arrived in .NET 9, over the monitor it replaced.
/// </summary>
/// <remarks>
/// The compiler recognises this type by its full name and lowers <c>lock (x)</c> over it to
/// <c>EnterScope</c>, so the one call site — <c>TimeZones.AddResolver</c>'s copy-on-write update —
/// reads and behaves exactly as it does on net10.0, on a monitor rather than on .NET 9's lock object.
/// </remarks>
// CS9216 says a Lock converted to another type locks the monitor rather than the lock. Here that is
// what the type is: the monitor is the implementation, and these four lines are the only place the
// conversion happens.
#pragma warning disable CS9216
internal sealed class Lock
{
    public Scope EnterScope()
    {
        Monitor.Enter(this);
        return new Scope(this);
    }

    public void Enter() => Monitor.Enter(this);

    public void Exit() => Monitor.Exit(this);

    public ref struct Scope
    {
        private Lock? owner;

        internal Scope(Lock owner) => this.owner = owner;

        public void Dispose()
        {
            if (owner is not null)
            {
                Monitor.Exit(owner);
                owner = null;
            }
        }
    }
}
#pragma warning restore CS9216
