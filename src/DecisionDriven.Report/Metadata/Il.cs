using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DecisionDriven.Report.Metadata;

/// <summary>
/// The member references in a method body: what it calls, and which fields it touches.
/// </summary>
/// <remarks>
/// System.Reflection.Metadata reads a body as bytes and leaves decoding it to the caller. This walks
/// the opcodes far enough to know each one's operand size - that is all it needs, because the only
/// operands it reads are the member tokens that contract usage and LCOM4 are made of.
/// </remarks>
internal static class Il
{
    /// <summary>What an instruction does with the member its token names.</summary>
    internal enum Use
    {
        /// <summary>Calls it, or takes its address to call later.</summary>
        Call,

        /// <summary>Reads or writes it: a field.</summary>
        Field,
    }

    /// <summary>Every call and field access in a body, in order.</summary>
    internal static IEnumerable<(Use Use, EntityHandle Member)> Members(MethodBodyBlock body)
    {
        BlobReader il = body.GetILReader();
        List<(Use, EntityHandle)> found = new List<(Use, EntityHandle)>();

        while (il.RemainingBytes > 0)
        {
            int code = il.ReadByte();
            if (code == 0xFE)
            {
                code = 0xFE00 | il.ReadByte();
            }

            switch (code)
            {
                // call, callvirt, newobj, ldftn, ldvirtftn
                case 0x28:
                case 0x6F:
                case 0x73:
                case 0xFE06:
                case 0xFE07:
                    found.Add((Use.Call, MetadataTokens.EntityHandle(il.ReadInt32())));
                    continue;

                // ldfld, ldflda, stfld, ldsfld, ldsflda, stsfld
                case 0x7B:
                case 0x7C:
                case 0x7D:
                case 0x7E:
                case 0x7F:
                case 0x80:
                    found.Add((Use.Field, MetadataTokens.EntityHandle(il.ReadInt32())));
                    continue;

                // switch: a count, then that many 4-byte targets.
                case 0x45:
                    int targets = il.ReadInt32();
                    il.Offset += targets * 4;
                    continue;
            }

            il.Offset += OperandSize(code);
        }

        return found;
    }

    /// <summary>Operand bytes after each opcode that is not handled above.</summary>
    private static int OperandSize(int code)
    {
        switch (code)
        {
            // ldarg.s, ldarga.s, starg.s, ldloc.s, ldloca.s, stloc.s, ldc.i4.s, unaligned., no.
            case 0x0E:
            case 0x0F:
            case 0x10:
            case 0x11:
            case 0x12:
            case 0x13:
            case 0x1F:
            case 0xDE:
            case 0xFE12:
            case 0xFE19:
                return 1;

            // ldarg, ldarga, starg, ldloc, ldloca, stloc
            case 0xFE09:
            case 0xFE0A:
            case 0xFE0B:
            case 0xFE0C:
            case 0xFE0D:
            case 0xFE0E:
                return 2;

            // ldc.i4, ldc.r4, jmp, calli, leave, and every token-taking opcode not handled above
            case 0x20:
            case 0x22:
            case 0x27:
            case 0x29:
            case 0xDD:
            case 0x70:
            case 0x71:
            case 0x72:
            case 0x74:
            case 0x75:
            case 0x79:
            case 0x81:
            case 0x8C:
            case 0x8D:
            case 0x8F:
            case 0xA3:
            case 0xA4:
            case 0xA5:
            case 0xC2:
            case 0xC6:
            case 0xD0:
            case 0xFE15:
            case 0xFE16:
            case 0xFE1C:
                return 4;

            // ldc.i8, ldc.r8
            case 0x21:
            case 0x23:
                return 8;
        }

        // The short branches (br.s to blt.un.s) and the long ones (br to blt.un).
        if (code >= 0x2B && code <= 0x37)
        {
            return 1;
        }

        if (code >= 0x38 && code <= 0x44)
        {
            return 4;
        }

        return 0;
    }
}
