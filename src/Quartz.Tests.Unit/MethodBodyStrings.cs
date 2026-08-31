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

#nullable enable

using System.Reflection;
using System.Reflection.Emit;

namespace Quartz.Tests.Unit;

/// <summary>
/// The string literals a compiled method names, read out of its IL.
/// </summary>
/// <remarks>
/// <para>
/// This is how a test asks "which of these strings does the product actually use", mechanically rather
/// than from a curated list that goes stale. It sees a <c>const</c> exactly as it sees a literal,
/// because the compiler inlines one into the other's place — which is the property that makes it worth
/// the IL walk: a name reaching a call site through a constant is still a name that call site says.
/// </para>
/// <para>
/// Two tests need it for unrelated reasons — the <c>quartz.*</c> keys the configuration readers
/// consult, and the span names the tracing store begins — so it lives here rather than in either.
/// </para>
/// </remarks>
internal static class MethodBodyStrings
{
    // Declared before anything uses them: static field initializers run in textual order, and the walk
    // needs these tables to know how long each instruction is.
    private static readonly OpCode?[] singleByteOpCodes = BuildOpCodeTable(twoByte: false);
    private static readonly OpCode?[] twoByteOpCodes = BuildOpCodeTable(twoByte: true);

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                          | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every string literal named by a method or constructor the type declares itself.
    /// </summary>
    /// <remarks>
    /// Nested types are not walked. A compiler-generated one — the display class holding a lambda —
    /// reports its declaring type's namespace, so a caller enumerating a namespace already has them;
    /// a caller naming one type asks for its nested types itself, and says why.
    /// </remarks>
    public static List<string> In(Type type)
    {
        List<string> literals = [];

        foreach (MethodBase method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            literals.AddRange(Of(method));
        }

        return literals;
    }

    /// <summary>
    /// Walks a method body instruction by instruction, resolving every <c>ldstr</c> against the
    /// module's string heap. Stepping over operands properly is what keeps an operand byte from being
    /// misread as an opcode and inventing literals that are not there.
    /// </summary>
    public static List<string> Of(MethodBase method)
    {
        List<string> literals = [];

        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            // abstract, extern or otherwise bodiless
            return literals;
        }

        if (il is null)
        {
            return literals;
        }

        Module module = method.Module;
        int position = 0;
        while (position < il.Length)
        {
            if (ReadOpCode(il, ref position) is not { } instruction)
            {
                // An opcode this table does not know means the walk has lost the instruction boundary;
                // stopping is honest, and each caller's "the scan found something" test notices if it
                // starts happening.
                break;
            }

            if (instruction.OperandType == OperandType.InlineString && position + 4 <= il.Length)
            {
                literals.Add(module.ResolveString(BitConverter.ToInt32(il, position)));
            }

            position += OperandSize(instruction, il, position);
        }

        return literals;
    }

    /// <summary>
    /// The IL opcode table, read off <see cref="OpCodes" /> rather than transcribed, so that the
    /// operand sizes the walk relies on come from the runtime's own definitions.
    /// </summary>
    private static OpCode?[] BuildOpCodeTable(bool twoByte)
    {
        OpCode?[] table = new OpCode?[0x100];
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            ushort value = unchecked((ushort) opCode.Value);
            if (twoByte && (value & 0xFF00) == 0xFE00)
            {
                table[value & 0xFF] = opCode;
            }
            else if (!twoByte && value < 0x100)
            {
                table[value] = opCode;
            }
        }

        return table;
    }

    private static OpCode? ReadOpCode(byte[] il, ref int position)
    {
        byte first = il[position++];
        if (first != 0xFE)
        {
            return singleByteOpCodes[first];
        }

        return position < il.Length ? twoByteOpCodes[il[position++]] : null;
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok
            or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
        _ => 0
    };
}
