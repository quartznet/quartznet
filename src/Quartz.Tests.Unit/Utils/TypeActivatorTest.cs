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

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// The residue of <c>ObjectUtils</c> after #3432: making an instance of a type that has a public
/// parameterless constructor, for the three callers that hold a <see cref="Type" /> and nothing else.
/// </summary>
/// <remarks>
/// The messages matter more than the mechanism. <c>DbProvider</c> reaches this with an ADO.NET driver's
/// connection type, and "Cannot instantiate type which has no empty constructor" naming that type is
/// what told #3341's step 7 that the trimmer had removed <c>SqliteConnection</c>'s constructor.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class TypeActivatorTest
{
    [Test]
    public void ATypeWithADefaultConstructorIsBuiltAsTheRequestedContract()
    {
        TypeActivator.Instantiate<IComparable>(typeof(Buildable))
            .Should().BeOfType<Buildable>();
    }

    [Test]
    public void ATypeWithNoDefaultConstructorIsNamedInTheFailure()
    {
        Action instantiating = () => TypeActivator.Instantiate<object>(typeof(NeedsArguments));

        instantiating.Should().Throw<ArgumentException>()
            .WithMessage("*no empty constructor*")
            .Which.ParamName.Should().Be(nameof(NeedsArguments),
                "the type is the only useful thing in the message when a trimmer has removed its constructor");
    }

    [Test]
    public void NoTypeAtAllIsItsOwnFailure()
    {
        Action instantiating = () => TypeActivator.Instantiate<object>(null);

        instantiating.Should().Throw<ArgumentNullException>(
            "a configuration key that named nothing must not surface as a NullReferenceException from inside reflection");
    }

    private sealed class Buildable : IComparable
    {
        public int CompareTo(object obj) => 0;
    }

    private sealed class NeedsArguments
    {
        public NeedsArguments(int required)
        {
            Required = required;
        }

        public int Required { get; }
    }
}
