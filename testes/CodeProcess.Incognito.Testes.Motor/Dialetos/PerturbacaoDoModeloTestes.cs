using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Testes.Conformidade;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// Cada propriedade de cada tipo do modelo do esquema tem fato que a confere, em cada caminho por onde o tipo aparece (a
/// colação de uma coluna e a padrão do banco são casos separados): alterada num objeto do banco falso completo que chegou
/// por aquele caminho, algum fato da suíte falha por asserção citando o objeto. As listas cuja ordem é do contrato também
/// são reordenadas. Um campo novo do modelo entra aqui sozinho, pela reflexão, e quebra este teste enquanto nenhum fato o
/// conferir.
/// </summary>
public sealed class PerturbacaoDoModeloTestes
{
    // Os tipos do modelo alcançáveis a partir do esquema lido.
    private static readonly Type[] _tipos =
    [
        typeof(EsquemaBanco), typeof(TabelaEsquema), typeof(ColunaEsquema), typeof(ChaveEstrangeira), typeof(ChaveParticao), typeof(SequenceEsquema), typeof(ObjetoResidual),
        typeof(PropriedadesBanco), typeof(CodificacaoTexto), typeof(ColacaoEsquema), typeof(LimiteComprimento), typeof(FaixaInteira), typeof(ReferenciasDaExpressao),
        typeof(IndiceUnico), typeof(ElementoIndice), typeof(RestricaoCheck), typeof(RestricaoExclusao), typeof(TriggerEsquema),
        typeof(AcessoLido), typeof(PermissoesBanco), typeof(PermissoesTabela), typeof(PermissoesResidual), typeof(PermissoesSequence),
    ];

    // As permissões do login: conferidas pelo fato do acesso, não pelo inventário.
    private static readonly Type[] _doAcesso = [typeof(AcessoLido), typeof(PermissoesBanco), typeof(PermissoesTabela), typeof(PermissoesResidual), typeof(PermissoesSequence)];

    // O que a falha precisa citar quando o objeto alterado não tem tabela nem outro dono com nome.
    private static readonly Dictionary<Type, string> _marcadores = new()
    {
        [typeof(PropriedadesBanco)] = "Propriedades do banco",
        [typeof(AcessoLido)] = "acesso do login",
        [typeof(PermissoesBanco)] = "permissões do banco",
    };

    private static readonly NullabilityInfoContext _anulabilidade = new();

    // Um nome que não existe no banco falso.
    private static readonly NomeObjeto _inexistente = new("loja", "inexistente");

    public static TheoryData<string, string, string, bool> Propriedades =>
    [
        .. _tipos.SelectMany(tipo => Caminhos(tipo).SelectMany(caminho => Alteraveis(tipo).Select(propriedade => (tipo.Name, caminho, propriedade.Name, false)))),
        .. _tipos.SelectMany(tipo => Caminhos(tipo).SelectMany(caminho => Alteraveis(tipo).Where(Reordenavel).Select(propriedade => (tipo.Name, caminho, propriedade.Name, true)))),
    ];

    [Fact]
    public void Todos_os_tipos_publicos_do_esquema_sao_perturbados()
    {
        // Os tipos com propriedades init do contrato (também aninhados, structs e em sub-namespaces), fora a identidade e o
        // transporte dela (que não são esquema, e têm fato e defeitos próprios) e o próprio contrato.
        var espaco = typeof(EsquemaBanco).Namespace!;
        var doModelo = typeof(EsquemaBanco).Assembly.GetTypes()
            .Where(tipo => (tipo.IsPublic || tipo.IsNestedPublic) && !tipo.IsEnum && !tipo.IsInterface
                && (tipo.Namespace == espaco || tipo.Namespace?.StartsWith(espaco + ".", StringComparison.Ordinal) == true)
                && Alteraveis(tipo).Any())
            .Where(tipo => tipo != typeof(IdentidadeLida) && tipo != typeof(TransporteConexao))
            .Select(tipo => tipo.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(doModelo, _tipos.Select(tipo => tipo.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task O_esquema_reconstruido_sem_alteracao_passa_em_tudo()
    {
        // O controle da reconstrução: sem alteração, a cópia diz o mesmo que o original e não derruba nada (senão toda
        // alteração pareceria pega).
        var (original, _) = await LerAsync();
        var falhas = await SuiteDeConformidadeTestes.RodarAsync(new FixtureFalsa(completo: true, Defeito.Nenhum, (esquema, _) => SemAlteracao(esquema)));

        Assert.Equal(DespejoDoModelo.Texto(original), DespejoDoModelo.Texto(SemAlteracao(original)));
        Assert.True(falhas.Count == 0, string.Join("\n", falhas.Select(falha => $"{falha.Key}: {falha.Value.Message}")));
    }

    [Theory]
    [MemberData(nameof(Propriedades))]
    public async Task A_alteracao_da_propriedade_e_pega_por_algum_fato(string tipo, string caminho, string propriedade, bool reordenar)
    {
        var classe = _tipos.Single(candidato => candidato.Name == tipo);
        var alvo = classe.GetProperty(propriedade)!;
        var (original, doDestino) = await LerAsync();
        var ocorrencias = Percorrer(original).Count(item => item.Objeto.GetType() == classe && item.Caminho == caminho);
        var tentadas = new List<string>();

        // O fato que precisa pegar: o das permissões, para os objetos delas; o inventário, para os do esquema.
        var fatoExigido = _doAcesso.Contains(classe) ? nameof(SuiteFalsa.O_acesso_e_lido_em_cada_lado) : nameof(SuiteFalsa.O_esquema_e_lido_sem_perdas);

        // Tenta cada objeto do tipo, na ordem do esquema, até um cuja alteração algum fato pegue. A alteração é calculada
        // antes de rodar a suíte, nos dois lados (cada um com as permissões dele): a que não cita nada (lista curta demais
        // para reordenar) pula o objeto sem rodá-la.
        for (var indice = 0; indice < ocorrencias; indice++)
        {
            var (perturbado, citado) = Alterar(original, classe, caminho, indice, alvo, reordenar);
            if (citado is null)
            {
                continue;
            }

            var destinoPerturbado = Alterar(doDestino, classe, caminho, indice, alvo, reordenar).Esquema;

            // A alteração aparece no despejo que a leitura estável e o destino usam.
            Assert.True(
                DespejoDoModelo.Texto(perturbado) != DespejoDoModelo.Texto(original),
                $"A alteração de {tipo}.{propriedade} (por {caminho}) em {citado} não muda o despejo do modelo.");
            // A alteração precisa ser pega pelo inventário (ou pelo fato das permissões), que compara o valor: a consistência só
            // pega o valor que quebra uma regra, e um valor errado que a cumpra passaria. Fora das tabelas e das permissões de
            // um objeto, pelo fato do próprio objeto.
            var falhas = await SuiteDeConformidadeTestes.RodarAsync(new FixtureFalsa(completo: true, Defeito.Nenhum, (_, destino) => destino ? destinoPerturbado : perturbado));
            var exigeFato = !_marcadores.ContainsValue(citado);
            if (falhas.Any(falha => falha.Value is IAssertionException && Cita(falha.Value.Message, citado)
                && (!exigeFato || falha.Key == fatoExigido)))
            {
                return;
            }

            tentadas.Add(citado);
        }

        Assert.Fail($"Nenhum fato da suíte pega a {(reordenar ? "reordenação" : "alteração")} de {tipo}.{propriedade} por {caminho} (objetos tentados: {string.Join(", ", tentadas)}).");
    }

    [Theory]
    [MemberData(nameof(Propriedades))]
    public async Task A_alteracao_da_propriedade_e_um_valor_valido_e_cita_um_dono(string tipo, string caminho, string propriedade, bool reordenar)
    {
        // Um valor que o init recusa (fora da faixa, nulo num obrigatório) faria o laço acima falhar por exceção, longe do
        // motivo; um citado vazio casaria com qualquer mensagem; e um campo nunca preenchido no falso nunca seria alterado.
        var classe = _tipos.Single(candidato => candidato.Name == tipo);
        var (original, _) = await LerAsync();
        var ocorrencias = Percorrer(original).Count(item => item.Objeto.GetType() == classe && item.Caminho == caminho);
        var citados = new List<string>();

        for (var indice = 0; indice < ocorrencias; indice++)
        {
            if (Alterar(original, classe, caminho, indice, classe.GetProperty(propriedade)!, reordenar).Citado is { } citado)
            {
                citados.Add(citado);
            }
        }

        Assert.True(citados.Count > 0, $"Nenhum objeto do banco falso completo tem {tipo}.{propriedade} alterável por {caminho}: preencher no banco falso.");
        Assert.DoesNotContain("", citados);
    }

    // A reconstrução sem alteração: nenhum objeto tem o número -1.
    private static EsquemaBanco SemAlteracao(EsquemaBanco esquema) =>
        Alterar(esquema, typeof(EsquemaBanco), "", -1, typeof(EsquemaBanco).GetProperty(nameof(EsquemaBanco.Tabelas))!).Esquema;

    // O nome inteiro: loja.pedidos não é citado por loja.pedidos_2025.
    private static bool Cita(string mensagem, string citado) =>
        Regex.IsMatch(mensagem, $@"(?<![\w.]){Regex.Escape(citado)}(?!\w)", RegexOptions.None, TimeSpan.FromSeconds(1));

    // As listas cuja ordem é do contrato: nomes (chave primária, colunas das chaves, outras mães, dependências, colunas sem
    // leitura), as colunas da tabela, os elementos de um índice (a ordem da chave), os objetos da tabela (ordem ordinal de
    // nome) e as permissões (a ordem do esquema).
    private static bool Reordenavel(PropertyInfo propriedade) =>
        propriedade.PropertyType == typeof(IReadOnlyList<string>)
        || propriedade.PropertyType == typeof(IReadOnlyList<NomeObjeto>)
        || propriedade.PropertyType == typeof(IReadOnlyList<ColunaEsquema>)
        || propriedade.PropertyType == typeof(IReadOnlyList<ElementoIndice>)
        || propriedade.PropertyType == typeof(IReadOnlyList<IndiceUnico>)
        || propriedade.PropertyType == typeof(IReadOnlyList<RestricaoCheck>)
        || propriedade.PropertyType == typeof(IReadOnlyList<RestricaoExclusao>)
        || propriedade.PropertyType == typeof(IReadOnlyList<TriggerEsquema>)
        || propriedade.PropertyType == typeof(IReadOnlyList<PermissoesTabela>)
        || propriedade.PropertyType == typeof(IReadOnlyList<PermissoesResidual>)
        || propriedade.PropertyType == typeof(IReadOnlyList<PermissoesSequence>);

    // O esquema lido da origem e o do destino (o mesmo, com as permissões de outro login).
    private static async Task<(EsquemaBanco Origem, EsquemaBanco Destino)> LerAsync()
    {
        var fixture = new FixtureFalsa(completo: true, Defeito.Nenhum);
        await using var par = await fixture.CriarParAsync(CancellationToken.None);
        return (await par.Origem.Esquema.LerAsync(CancellationToken.None), await par.Destino.Esquema.LerAsync(CancellationToken.None));
    }

    private static IEnumerable<PropertyInfo> Alteraveis(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(propriedade => propriedade.SetMethod is { IsPublic: true })
            .OrderBy(propriedade => propriedade.MetadataToken);

    // Os caminhos por onde um tipo do modelo aparece: tipo e propriedade que o guardam (a raiz não tem caminho).
    private static IEnumerable<string> Caminhos(Type tipo) => tipo == typeof(EsquemaBanco)
        ? [""]
        : _tipos.SelectMany(pai => Alteraveis(pai)
            .Where(propriedade => (ElementoDoModelo(propriedade.PropertyType) ?? propriedade.PropertyType) == tipo)
            .Select(propriedade => $"{pai.Name}.{propriedade.Name}"));

    // Os objetos do modelo, na ordem do esquema, cada um com o nome que uma falha precisa citar (o da tabela, sequence ou
    // objeto residual dono dele; a chave estrangeira, pelo nome dela) e o caminho por onde chegou.
    private static IEnumerable<(object Objeto, string Dono, string Caminho)> Percorrer(object objeto, string dono = "", string caminho = "")
    {
        dono = objeto switch
        {
            TabelaEsquema tabela => tabela.Nome.ToString(),
            SequenceEsquema sequence => sequence.Nome.ToString(),
            ObjetoResidual residual => residual.Nome.ToString(),
            ChaveEstrangeira chave => chave.Nome,
            PermissoesTabela permissoes => permissoes.Tabela.ToString(),
            PermissoesResidual permissoes => permissoes.Nome.ToString(),
            PermissoesSequence permissoes => permissoes.Nome.ToString(),
            _ when _marcadores.TryGetValue(objeto.GetType(), out var marcador) => marcador,
            _ => dono,
        };
        yield return (objeto, dono, caminho);
        foreach (var propriedade in Alteraveis(objeto.GetType()))
        {
            var valor = propriedade.GetValue(objeto);
            var filhos = valor is IEnumerable itens and not string ? itens.Cast<object>() : valor is null ? [] : [valor];
            foreach (var filho in filhos.Where(filho => _tipos.Contains(filho.GetType())))
            {
                foreach (var item in Percorrer(filho, dono, Caminho(objeto, propriedade)))
                {
                    yield return item;
                }
            }
        }
    }

    // Reconstrói o esquema com a propriedade alterada (ou reordenada) no objeto de número 'indice' do tipo, entre os que
    // chegaram pelo caminho; devolve o que a falha precisa citar, ou nulo se a propriedade não tem alteração possível nesse
    // objeto.
    private static (EsquemaBanco Esquema, string? Citado) Alterar(EsquemaBanco esquema, Type tipo, string caminhoAlvo, int indice, PropertyInfo alvo, bool reordenar = false)
    {
        var contador = 0;
        string? citado = null;

        object Reconstruir(object objeto, string dono, string caminho)
        {
            dono = Percorrer(objeto, dono).First().Dono;
            var copia = Activator.CreateInstance(objeto.GetType())!;
            var proprio = objeto.GetType() == tipo && caminho == caminhoAlvo && contador++ == indice;
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
                            ? Lista(elemento, itens.Select(item => Reconstruir(item, dono, Caminho(objeto, propriedade))))
                            : Lista(propriedade.PropertyType.GetGenericArguments()[0], itens);
                    }
                    else
                    {
                        valor = Copiar(valor, propriedade.PropertyType, dono, Caminho(objeto, propriedade));
                    }
                }
                else if (proprio && propriedade.Name == alvo.Name)
                {
                    var (novo, cita) = Novo(valor, propriedade, dono);
                    if (cita is not null)
                    {
                        citado = cita;
                        valor = novo;
                    }
                }
                else
                {
                    valor = Copiar(valor, propriedade.PropertyType, dono, Caminho(objeto, propriedade));
                }

                propriedade.SetValue(copia, valor);
            }

            return copia;
        }

        object? Copiar(object? valor, Type declarado, string dono, string caminho) => valor switch
        {
            null => null,
            _ when _tipos.Contains(valor.GetType()) => Reconstruir(valor, dono, caminho),
            IEnumerable itens and not string when ElementoDoModelo(declarado) is { } elemento =>
                Lista(elemento, itens.Cast<object>().Select(item => Reconstruir(item, dono, caminho))),
            _ => valor,
        };

        var novo = (EsquemaBanco)Reconstruir(esquema, "", "");
        return (novo, citado);
    }

    // O valor alterado, sempre válido para o init e diferente, e o que a falha precisa citar: o dono, o nome do item tirado
    // de uma lista do modelo ou, na raiz, o marcador do tipo da propriedade.
    private static (object? Valor, string? Citado) Novo(object? valor, PropertyInfo propriedade, string dono)
    {
        var tipo = propriedade.PropertyType;
        if (dono.Length == 0 && _marcadores.TryGetValue(tipo, out var marcador))
        {
            dono = marcador;
        }

        var anulavel = Nullable.GetUnderlyingType(tipo);
        if (anulavel == typeof(bool))
        {
            // Três estados: "não informa" é diferente de falso.
            return (valor switch { null => true, true => false, _ => null }, dono);
        }

        if ((anulavel ?? tipo) == typeof(int))
        {
            return (valor is int numero ? (numero > 1 ? numero - 1 : numero + 1) : 1, dono);
        }

        if (tipo == typeof(long))
        {
            var numero = (long)valor!;
            return (numero > 1 ? numero - 1 : numero + 1, dono);
        }

        if (valor is Version versao)
        {
            return (new Version(versao.Major, versao.Minor + 1), dono);
        }

        if (_tipos.Contains(tipo))
        {
            // Pela anulabilidade declarada: o anulável preenchido vira nulo, o nulo vira uma instância mínima, e o obrigatório
            // vira outra instância, com o primeiro campo alterado.
            var podeSerNulo = _anulabilidade.Create(propriedade).WriteState == NullabilityState.Nullable;
            return (valor, podeSerNulo) switch
            {
                (null, _) => (Minimo(tipo), dono),
                (_, true) => (null, dono),
                _ => (Outro(valor, dono), dono),
            };
        }
        if ((anulavel ?? tipo).IsEnum)
        {
            var valores = Enum.GetValues(anulavel ?? tipo).Cast<object>().ToArray();
            var atual = valor is null ? -1 : Array.IndexOf(valores, valor);
            return (anulavel is not null && atual == valores.Length - 1 ? null : valores[(atual + 1) % valores.Length], dono);
        }

        if (ElementoDoModelo(tipo) is { } elemento)
        {
            // Sem o primeiro item; a lista que não pode ficar vazia (os elementos de um índice) troca o único item.
            var itens = ((IEnumerable)valor!).Cast<object>().ToArray();
            return itens switch
            {
                [] => (valor, null),
                [var unico] when propriedade.Name == nameof(IndiceUnico.Elementos) => (Lista(elemento, [Outro(unico, dono)]), dono),
                _ => (Lista(elemento, itens.Skip(1)), Percorrer(itens[0]).First().Dono is { Length: > 0 } nome && nome != dono ? nome : dono),
            };
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
            _ => throw new InvalidOperationException($"Sem alteração para o tipo {tipo.Name}: acrescentar aqui."),
        };
    }

    // A instância mínima válida de um objeto do modelo, para uma propriedade anulável que estava nula.
    private static object Minimo(Type tipo) => tipo.Name switch
    {
        nameof(ChaveParticao) => new ChaveParticao { Colunas = ["x"], ColunasIncompletas = false },
        nameof(ColacaoEsquema) => new ColacaoEsquema { Nome = "x", Deterministica = true, TemRegras = false, IgnoraCaixa = false, IgnoraAcento = false, IgnoraPontuacao = false },
        nameof(LimiteComprimento) => new LimiteComprimento { Valor = 1, Unidade = UnidadeComprimento.Bytes },
        nameof(FaixaInteira) => new FaixaInteira { Minimo = 0, Maximo = 1 },
        nameof(ReferenciasDaExpressao) => new ReferenciasDaExpressao { Colunas = [], Incompletas = false },
        nameof(PropriedadesBanco) => new PropriedadesBanco
        {
            Versao = new Version(1, 0),
            Codificacao = new CodificacaoTexto { Nome = "x", Definida = true, Unicode = true, BytesPorCaractereMaximo = 4 },
            Colacao = (ColacaoEsquema)Minimo(typeof(ColacaoEsquema)),
        },
        _ => throw new InvalidOperationException($"Sem instância mínima para o tipo {tipo.Name}: acrescentar aqui."),
    };

    // Outra instância de um objeto obrigatório: a mesma, com a primeira propriedade alterada.
    private static object Outro(object valor, string dono)
    {
        var copia = Activator.CreateInstance(valor.GetType())!;
        var primeira = true;
        foreach (var propriedade in Alteraveis(valor.GetType()))
        {
            var atual = propriedade.GetValue(valor);
            propriedade.SetValue(copia, primeira ? Novo(atual, propriedade, dono).Valor : atual);
            primeira = false;
        }

        return copia;
    }

    private static string Caminho(object dono, PropertyInfo propriedade) => $"{dono.GetType().Name}.{propriedade.Name}";

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
