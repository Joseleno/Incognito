using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Conformidade;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// Cada propriedade de cada tipo do modelo do esquema tem fato que a confere: alterada num objeto do banco falso
/// completo, algum fato da suíte falha por asserção citando o objeto. As listas cuja ordem é do contrato também são
/// reordenadas. Um campo novo do modelo entra aqui sozinho, pela reflexão, e quebra este teste enquanto nenhum fato o
/// conferir.
/// </summary>
public sealed class PerturbacaoDoModeloTestes
{
    // Os tipos do modelo alcançáveis a partir do esquema lido.
    private static readonly Type[] _tipos =
        [typeof(EsquemaBanco), typeof(TabelaEsquema), typeof(ColunaEsquema), typeof(ChaveEstrangeira), typeof(ChaveParticao), typeof(SequenceEsquema), typeof(ObjetoResidual)];

    // Um nome que não existe no banco falso.
    private static readonly NomeObjeto _inexistente = new("loja", "inexistente");

    public static TheoryData<string, string, bool> Propriedades =>
    [
        .. _tipos.SelectMany(tipo => Alteraveis(tipo).Select(propriedade => (tipo.Name, propriedade.Name, false))),
        .. _tipos.SelectMany(tipo => Alteraveis(tipo).Where(Reordenavel).Select(propriedade => (tipo.Name, propriedade.Name, true))),
    ];

    [Fact]
    public void Todos_os_tipos_publicos_do_esquema_sao_perturbados()
    {
        // Os tipos com propriedades init do contrato (também aninhados, structs e em sub-namespaces), fora a identidade
        // (que não é esquema) e o próprio contrato.
        var espaco = typeof(EsquemaBanco).Namespace!;
        var doModelo = typeof(EsquemaBanco).Assembly.GetTypes()
            .Where(tipo => (tipo.IsPublic || tipo.IsNestedPublic) && !tipo.IsEnum && !tipo.IsInterface
                && (tipo.Namespace == espaco || tipo.Namespace?.StartsWith(espaco + ".", StringComparison.Ordinal) == true)
                && Alteraveis(tipo).Any())
            .Where(tipo => tipo != typeof(IdentidadeLida))
            .Select(tipo => tipo.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(doModelo, _tipos.Select(tipo => tipo.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task O_esquema_reconstruido_sem_alteracao_passa_em_tudo()
    {
        // O controle da reconstrução: sem alteração, a cópia diz o mesmo que o original e não derruba nada (senão toda
        // alteração pareceria pega).
        var original = await LerAsync();
        var falhas = await SuiteDeConformidadeTestes.RodarAsync(new FixtureFalsa(completo: true, Defeito.Nenhum, SemAlteracao));

        Assert.Equal(DespejoDoModelo.Texto(original), DespejoDoModelo.Texto(SemAlteracao(original)));
        Assert.True(falhas.Count == 0, string.Join("\n", falhas.Select(falha => $"{falha.Key}: {falha.Value.Message}")));
    }

    [Theory]
    [MemberData(nameof(Propriedades))]
    public async Task A_alteracao_da_propriedade_e_pega_por_algum_fato(string tipo, string propriedade, bool reordenar)
    {
        var classe = _tipos.Single(candidato => candidato.Name == tipo);
        var alvo = classe.GetProperty(propriedade)!;
        var original = await LerAsync();
        var ocorrencias = Percorrer(original).Count(item => item.Objeto.GetType() == classe);
        var tentadas = new List<string>();

        // Tenta cada objeto do tipo, na ordem do esquema, até um cuja alteração algum fato pegue.
        for (var indice = 0; indice < ocorrencias; indice++)
        {
            string? citado = null;
            EsquemaBanco? perturbado = null;
            EsquemaBanco Perturbar(EsquemaBanco esquema)
            {
                var (alterado, cita) = Alterar(esquema, classe, indice, alvo, reordenar);
                citado ??= cita;
                perturbado ??= alterado;
                return alterado;
            }

            var falhas = await SuiteDeConformidadeTestes.RodarAsync(new FixtureFalsa(completo: true, Defeito.Nenhum, Perturbar));
            if (citado is null)
            {
                continue;
            }

            // A alteração aparece no despejo que a leitura estável e o destino usam.
            Assert.True(
                DespejoDoModelo.Texto(perturbado) != DespejoDoModelo.Texto(original),
                $"A alteração de {tipo}.{propriedade} em {citado} não muda o despejo do modelo.");
            if (falhas.Values.Any(falha => falha is IAssertionException && Cita(falha.Message, citado)))
            {
                return;
            }

            tentadas.Add(citado);
        }

        Assert.Fail($"Nenhum fato da suíte pega a {(reordenar ? "reordenação" : "alteração")} de {tipo}.{propriedade} (objetos tentados: {string.Join(", ", tentadas)}).");
    }

    // A reconstrução sem alteração: nenhum objeto tem o número -1.
    private static EsquemaBanco SemAlteracao(EsquemaBanco esquema) =>
        Alterar(esquema, typeof(EsquemaBanco), -1, typeof(EsquemaBanco).GetProperty(nameof(EsquemaBanco.Tabelas))!).Esquema;

    // O nome inteiro: loja.pedidos não é citado por loja.pedidos_2025.
    private static bool Cita(string mensagem, string citado) =>
        Regex.IsMatch(mensagem, $@"(?<![\w.]){Regex.Escape(citado)}(?!\w)", RegexOptions.None, TimeSpan.FromSeconds(1));

    // As listas cuja ordem é do contrato: nomes (chave primária, colunas das chaves, outras mães) e as colunas da tabela.
    private static bool Reordenavel(PropertyInfo propriedade) =>
        propriedade.PropertyType == typeof(IReadOnlyList<string>)
        || propriedade.PropertyType == typeof(IReadOnlyList<NomeObjeto>)
        || propriedade.PropertyType == typeof(IReadOnlyList<ColunaEsquema>);

    private static async Task<EsquemaBanco> LerAsync()
    {
        var fixture = new FixtureFalsa(completo: true, Defeito.Nenhum);
        await using var par = await fixture.CriarParAsync(CancellationToken.None);
        return await par.Origem.Esquema.LerAsync(CancellationToken.None);
    }

    private static IEnumerable<PropertyInfo> Alteraveis(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(propriedade => propriedade.SetMethod is { IsPublic: true })
            .OrderBy(propriedade => propriedade.MetadataToken);

    // Os objetos do modelo, na ordem do esquema, cada um com o nome que uma falha precisa citar: o da tabela, sequence ou
    // objeto residual dono dele (a chave estrangeira, pelo nome dela).
    private static IEnumerable<(object Objeto, string Dono)> Percorrer(object objeto, string dono = "")
    {
        dono = objeto switch
        {
            TabelaEsquema tabela => tabela.Nome.ToString(),
            SequenceEsquema sequence => sequence.Nome.ToString(),
            ObjetoResidual residual => residual.Nome.ToString(),
            ChaveEstrangeira chave => chave.Nome,
            _ => dono,
        };
        yield return (objeto, dono);
        foreach (var propriedade in Alteraveis(objeto.GetType()))
        {
            var valor = propriedade.GetValue(objeto);
            var filhos = valor is IEnumerable itens and not string ? itens.Cast<object>() : valor is null ? [] : [valor];
            foreach (var filho in filhos.Where(filho => _tipos.Contains(filho.GetType())))
            {
                foreach (var item in Percorrer(filho, dono))
                {
                    yield return item;
                }
            }
        }
    }

    // Reconstrói o esquema com a propriedade alterada (ou reordenada) no objeto de número 'indice' do tipo; devolve o que a
    // falha precisa citar, ou nulo se a propriedade não tem alteração possível nesse objeto.
    private static (EsquemaBanco Esquema, string? Citado) Alterar(EsquemaBanco esquema, Type tipo, int indice, PropertyInfo alvo, bool reordenar = false)
    {
        var contador = 0;
        string? citado = null;

        object Reconstruir(object objeto, string dono)
        {
            dono = Percorrer(objeto, dono).First().Dono;
            var copia = Activator.CreateInstance(objeto.GetType())!;
            var proprio = objeto.GetType() == tipo && contador++ == indice;
            foreach (var propriedade in Alteraveis(objeto.GetType()))
            {
                var valor = propriedade.GetValue(objeto);
                if (proprio && propriedade.Name == alvo.Name && reordenar)
                {
                    var itens = ((IEnumerable)valor!).Cast<object>().Reverse().ToArray();
                    if (itens.Length > 1)
                    {
                        citado = dono;
                        valor = ElementoDoModelo(propriedade.PropertyType) is { } elemento
                            ? Lista(elemento, itens.Select(item => Reconstruir(item, dono)))
                            : Lista(propriedade.PropertyType.GetGenericArguments()[0], itens);
                    }
                    else
                    {
                        valor = Copiar(valor, propriedade.PropertyType, dono);
                    }
                }
                else if (proprio && propriedade.Name == alvo.Name)
                {
                    var (novo, cita) = Novo(valor, propriedade.PropertyType, dono);
                    if (cita is not null)
                    {
                        citado = cita;
                        valor = novo;
                    }
                }
                else
                {
                    valor = Copiar(valor, propriedade.PropertyType, dono);
                }

                propriedade.SetValue(copia, valor);
            }

            return copia;
        }

        object? Copiar(object? valor, Type declarado, string dono) => valor switch
        {
            null => null,
            _ when _tipos.Contains(valor.GetType()) => Reconstruir(valor, dono),
            IEnumerable itens and not string when ElementoDoModelo(declarado) is { } elemento =>
                Lista(elemento, itens.Cast<object>().Select(item => Reconstruir(item, dono))),
            _ => valor,
        };

        var novo = (EsquemaBanco)Reconstruir(esquema, "");
        return (novo, citado);
    }

    // O valor alterado e o que a falha precisa citar: o dono, ou o nome do item tirado de uma lista do modelo.
    private static (object? Valor, string? Citado) Novo(object? valor, Type tipo, string dono)
    {
        var anulavel = Nullable.GetUnderlyingType(tipo);
        if ((anulavel ?? tipo).IsEnum)
        {
            var valores = Enum.GetValues(anulavel ?? tipo).Cast<object>().ToArray();
            var atual = valor is null ? -1 : Array.IndexOf(valores, valor);
            return (anulavel is not null && atual == valores.Length - 1 ? null : valores[(atual + 1) % valores.Length], dono);
        }

        if (ElementoDoModelo(tipo) is { } elemento)
        {
            var itens = ((IEnumerable)valor!).Cast<object>().ToArray();
            return itens.Length == 0 ? (valor, null) : (Lista(elemento, itens.Skip(1)), Percorrer(itens[0]).First().Dono is { Length: > 0 } nome && nome != dono ? nome : dono);
        }

        return valor switch
        {
            bool sim => (!sim, dono),
            string texto => (texto + "x", dono),
            NomeObjeto nome => (new NomeObjeto(nome.Esquema, nome.Nome + "x"), dono),
            IReadOnlyList<string> nomes => (nomes.Count > 0 ? nomes.Take(nomes.Count - 1).ToArray() : new[] { "x" }, dono),
            IReadOnlyList<NomeObjeto> nomes => (nomes.Count > 0 ? nomes.Take(nomes.Count - 1).ToArray() : new[] { _inexistente }, dono),
            IReadOnlyList<ReferenciaColuna> colunas => (colunas.Count > 0 ? colunas.Take(colunas.Count - 1).ToArray() : new[] { new ReferenciaColuna(_inexistente, "x") }, dono),
            null when tipo == typeof(string) => ("x", dono),
            null when tipo == typeof(NomeObjeto) => (_inexistente, dono),
            null when tipo == typeof(ChaveParticao) => (new ChaveParticao { Colunas = ["x"] }, dono),
            null => throw new InvalidOperationException($"Sem alteração para o tipo {tipo.Name}: acrescentar aqui."),
            _ when _tipos.Contains(valor.GetType()) => (null, dono),
            _ => throw new InvalidOperationException($"Sem alteração para o tipo {tipo.Name}: acrescentar aqui."),
        };
    }

    // O tipo do item de uma lista de objetos do modelo; nulo nas outras.
    private static Type? ElementoDoModelo(Type tipo) =>
        tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && _tipos.Contains(tipo.GetGenericArguments()[0])
            ? tipo.GetGenericArguments()[0]
            : null;

    private static Array Lista(Type elemento, IEnumerable<object> itens)
    {
        var origem = itens.ToArray();
        var lista = Array.CreateInstance(elemento, origem.Length);
        Array.Copy(origem, lista, origem.Length);
        return lista;
    }
}
