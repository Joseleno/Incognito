using System.Reflection;
using System.Runtime.CompilerServices;
using CodeProcess.Incognito.Dialetos;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// O motor chama o dialeto só pelo envoltório (<c>DialetoProtegido</c>): fora dele e do próprio contrato, nenhum método do
/// motor chama um ponto de extensão do contrato nem guarda um banco conectado ou uma parte dele. Assim toda exceção de
/// dialeto passa pela tradução, que nunca deixa sair a mensagem, os dados nem a interna do driver.
/// </summary>
public sealed class FronteiraDoDialetoTestes
{
    private const string Envoltorio = "CodeProcess.Incognito.Interno.DialetoProtegido";
    private const string Contrato = "CodeProcess.Incognito.Dialetos";

    // Os pontos de extensão: chamar qualquer membro deles é chamar código do dialeto.
    private static readonly Type[] _extensoes =
    [
        typeof(ProvedorDialeto), typeof(DialetoBanco), typeof(DialetoOrigem), typeof(DialetoDestino),
        typeof(LeitorEsquema), typeof(IdentidadeBanco), typeof(DialetoSql),
    ];

    // As partes que só existem depois do conectar, que só o envoltório chama: ninguém mais as guarda.
    private static readonly Type[] _conectados = [typeof(DialetoBanco), typeof(LeitorEsquema), typeof(IdentidadeBanco), typeof(DialetoSql)];

    [Fact]
    public void So_o_envoltorio_chama_o_dialeto_e_guarda_o_banco_conectado()
    {
        var motor = typeof(ProvedorDialeto).Assembly;
        var violacoes = motor.GetTypes().Where(Vigiado).SelectMany(Violacoes).ToArray();

        Assert.True(violacoes.Length == 0, $"Chamada ou posse do dialeto fora do envoltório: {string.Join("; ", violacoes)}.");
        Assert.NotNull(motor.GetType(Envoltorio));
    }

    [Fact]
    public void A_guarda_acha_a_chamada_em_metodo_assincrono_em_lambda_e_por_interpolacao()
    {
        // Controle positivo: as máquinas de estado e os fechamentos gerados pelo compilador entram na leitura, atribuídos ao
        // tipo de fora; e a seleção vigia o planejador e o controle e isenta só o envoltório e o contrato público.
        var motor = typeof(ProvedorDialeto).Assembly;
        Assert.True(Vigiado(typeof(ChamaODialeto)));
        Assert.True(Vigiado(motor.GetType("CodeProcess.Incognito.Interno.PlanejadorInterno", throwOnError: true)!));
        Assert.False(Vigiado(motor.GetType(Envoltorio, throwOnError: true)!.GetNestedTypes(BindingFlags.NonPublic).First()));
        Assert.False(Vigiado(typeof(LeitorEsquema)));
        var violacoes = typeof(ChamaODialeto).Assembly.GetTypes()
            .Where(tipo => tipo == typeof(ChamaODialeto) || tipo.DeclaringType == typeof(ChamaODialeto))
            .SelectMany(Violacoes)
            .ToArray();

        Assert.Contains(violacoes, violacao => violacao.Contains("LeitorEsquema.LerAsync", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("DialetoSql.Citar", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("AppendFormatted", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("guarda IdentidadeBanco", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("ProvedorDialeto.ConectarOrigemAsync", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("Delegado chama LeitorEsquema.LerAsync", StringComparison.Ordinal));
        Assert.Contains(violacoes, violacao => violacao.Contains("_sql guarda DialetoSql", StringComparison.Ordinal));
    }

    // Fora do envoltório, todo tipo do motor é vigiado, salvo os tipos visíveis do contrato (e os gerados dentro deles); um
    // tipo interno com o namespace do contrato não fica isento.
    private static bool Vigiado(Type tipo)
    {
        var externo = Externo(tipo)!;
        var doContrato = externo.IsVisible && (externo.Namespace == Contrato || externo.Namespace?.StartsWith(Contrato + ".", StringComparison.Ordinal) == true);
        return externo.FullName != Envoltorio && !doContrato;
    }

    // O tipo de primeiro nível: as máquinas de estado e os fechamentos ficam aninhados no tipo que os escreveu.
    private static Type? Externo(Type tipo)
    {
        var atual = tipo;
        while (atual.DeclaringType is { } dono)
        {
            atual = dono;
        }

        return atual;
    }

    private static IEnumerable<string> Violacoes(Type tipo)
    {
        const BindingFlags Todos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var campo in tipo.GetFields(Todos).Where(campo => Conectado(campo.FieldType)))
        {
            yield return $"{tipo.FullName}.{campo.Name} guarda {campo.FieldType.Name}";
        }

        foreach (var metodo in tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)))
        {
            var onde = $"{tipo.FullName}.{metodo.Name}";
            var tipos = metodo.GetParameters().Select(parametro => parametro.ParameterType)
                .Concat(metodo is MethodInfo info ? [info.ReturnType] : [])
                .Concat(metodo.GetMethodBody()?.LocalVariables.Select(local => local.LocalType) ?? []);
            foreach (var guardado in tipos.Where(Conectado).Distinct())
            {
                yield return $"{onde} guarda {guardado.Name}";
            }

            foreach (var citado in MembrosNoIl.Citados(metodo).OfType<MethodBase>())
            {
                if (citado.DeclaringType is { } dono && _extensoes.Any(extensao => extensao.IsAssignableFrom(dono)))
                {
                    yield return $"{onde} chama {dono.Name}.{citado.Name}";
                }
                else if (citado is MethodInfo { IsGenericMethod: true } generico && generico.GetGenericArguments().Any(Extensao))
                {
                    yield return $"{onde} chama {citado.Name} com o dialeto como argumento de tipo";
                }
            }
        }
    }

    private static bool Extensao(Type tipo) => _extensoes.Any(extensao => extensao.IsAssignableFrom(tipo)) || (tipo.IsGenericType && tipo.GetGenericArguments().Any(Extensao));

    private static bool Conectado(Type tipo)
    {
        var atual = tipo.IsByRef || tipo.IsArray ? tipo.GetElementType()! : tipo;
        return _conectados.Any(conectado => conectado.IsAssignableFrom(atual)) || (atual.IsGenericType && atual.GetGenericArguments().Any(Conectado));
    }

    // Chama o dialeto como o motor nunca pode: num método assíncrono, numa lambda, por interpolação e guardando a parte.
    private static class ChamaODialeto
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static async Task<int> LerAsync(LeitorEsquema leitor) => (await leitor.LerAsync(CancellationToken.None)).Tabelas.Count;

        internal static Func<DialetoSql, string> Citar() => sql => sql.Citar("cliente");

        internal static string Texto(ProvedorDialeto provedor) => $"provedor {provedor}";

        internal static Task<DialetoOrigem> Conectar(ProvedorDialeto provedor) => provedor.ConectarOrigemAsync(new SegredoConexao("x"), CancellationToken.None);

        private static DialetoSql? _sql;

        internal static IdentidadeBanco Guardar(IdentidadeBanco identidade) => identidade;

        internal static void Lembrar(DialetoSql sql) => _sql = sql;

        internal static string? Lembrado() => _sql?.ToString();

        internal static Func<CancellationToken, Task<EsquemaBanco>> Delegado(LeitorEsquema leitor) => leitor.LerAsync;
    }
}
