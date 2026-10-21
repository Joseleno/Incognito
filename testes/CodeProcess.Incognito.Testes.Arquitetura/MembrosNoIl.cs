using System.Reflection;
using System.Reflection.Emit;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Os membros (métodos, campos e tipos) que o IL de um método cita, inclusive no corpo. Cópia consciente da do projeto de
/// testes do núcleo (que é interna e que este projeto não referencia).
/// </summary>
/// <remarks>
/// Lê só o fluxo de instruções: não vê o tipo de um <c>catch</c>, das variáveis locais nem dos atributos, nem acesso
/// por reflexão com o nome em texto.
/// </remarks>
internal static class MembrosNoIl
{
    private static readonly Dictionary<short, OpCode> _opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(campo => (OpCode)campo.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);

    public static IEnumerable<MemberInfo> Citados(MethodBase metodo)
    {
        var il = metodo.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var tipo = metodo.DeclaringType!;
        var argumentosDoTipo = tipo.IsGenericType ? tipo.GetGenericArguments() : null;
        var argumentosDoMetodo = metodo.IsGenericMethod ? metodo.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            OpCode opcode;
            if (il[i] == 0xFE)
            {
                opcode = _opcodes[unchecked((short)(0xFE00 | il[i + 1]))];
                i += 2;
            }
            else
            {
                opcode = _opcodes[il[i]];
                i++;
            }

            switch (opcode.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    yield return metodo.Module.ResolveMember(BitConverter.ToInt32(il, i), argumentosDoTipo, argumentosDoMetodo)!;
                    i += 4;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + (4 * BitConverter.ToInt32(il, i));
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineNone:
                    break;
                default:
                    i += 4;
                    break;
            }
        }
    }

    /// <summary>O tipo que declara o membro (ou o próprio, quando é um tipo).</summary>
    public static Type? Dono(MemberInfo membro) => membro as Type ?? membro.DeclaringType;
}
