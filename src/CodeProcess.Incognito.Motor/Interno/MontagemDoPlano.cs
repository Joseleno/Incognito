// A montagem lê o esquema que o dialeto devolveu, tipos do contrato experimental de dialeto que o motor declara; nada
// daqui é público.
#pragma warning disable INCX001

using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Configuracao.Leitura;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>O que a montagem devolve: as tabelas e os avisos que o plano publica, e a carga da execução.</summary>
/// <param name="Tabelas">As unidades de cópia, em ordem ordinal de esquema e tabela.</param>
/// <param name="Avisos">Os avisos, em ordem e sem repetição.</param>
/// <param name="Execucao">A carga da execução, com as unidades na mesma ordem.</param>
internal sealed record PlanoMontado(IReadOnlyList<MapaTabela> Tabelas, IReadOnlyList<Aviso> Avisos, DadosDeExecucao Execucao);

/// <summary>Se um nome escrito designa um nome do catálogo, pela regra do banco de um lado (o envoltório do dialeto).</summary>
/// <param name="escrito">O nome como foi escrito, sem as aspas.</param>
/// <param name="entreAspas">Se foi escrito entre aspas.</param>
/// <param name="doCatalogo">O nome como o catálogo o guarda.</param>
/// <returns>A correspondência.</returns>
internal delegate CorrespondenciaNome ComparadorDeNomes(string escrito, bool entreAspas, string doCatalogo);

/// <summary>
/// Do esquema lido e das regras resolvidas ao plano: as unidades de cópia e o pareamento entre origem e destino (P3), os
/// nomes da configuração resolvidos contra o catálogo, que barram o resto quando falham (P4), e as unidades, as ações,
/// as estrangeiras, o esquema reservado e a origem contra o destino (P5), com o tipo, os limites e os nulos de cada
/// coluna tratada e a criação do transformador dela numa sessão descartável (P6). Os nomes casam só pelo dialeto
/// (<see cref="ComparadorDeNomes"/>), nunca pelo texto: o casamento exato nunca vence, e a caixa diferente só vira dica.
/// </summary>
internal sealed class MontagemDoPlano
{
    // O esquema do diário do Incognito, e os de sistema do PostgreSQL (os do SQL Server vêm com o dialeto dele): uma entrada
    // neles nunca é tabela de cópia.
    private const string Reservado = "incognito";

    // A maior precisão que o decimal do .NET representa sem perda.
    private const int MaiorPrecisao = 28;
    private static readonly string[] _sistema = ["information_schema", "pg_catalog", "pg_toast"];

    private readonly ConfiguracaoIncognito _configuracao;
    private readonly ResolucaoDeRegras _regras;
    private readonly RegistroTransformadores _registro;
    private readonly CriacaoNoPlano _criacao;
    private readonly ComparadorDeNomes _daOrigem;
    private readonly ComparadorDeNomes _doDestino;
    private readonly LeituraDoBanco _origem;
    private readonly LeituraDoBanco _destino;
    private readonly ColetorDoPlano _coletor = new();

    private readonly List<Unidade> _unidades = [];
    private readonly Dictionary<TabelaEsquema, Unidade> _unidadeDe = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Unidade, RegraTabelaEscrita> _entradas = [];
    private readonly Dictionary<RegraColunaEscrita, ColunaEsquema> _colunas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RegraColunaEscrita, ColunaEsquema> _derivadas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RegraColunaEscrita, ColunaResolvida> _resolvidas = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Unidade, (AcaoTabela Acao, bool Escrita)> _acoes = [];
    private readonly Dictionary<RegraColunaEscrita, ContextoDaColuna> _contextos = new(ReferenceEqualityComparer.Instance);

    private MontagemDoPlano(
        ConfiguracaoIncognito configuracao,
        ResolucaoDeRegras regras,
        RegistroTransformadores registro,
        CriacaoNoPlano criacao,
        ComparadorDeNomes daOrigem,
        ComparadorDeNomes doDestino,
        LeituraDoBanco origem,
        LeituraDoBanco destino)
    {
        _configuracao = configuracao;
        _regras = regras;
        _registro = registro;
        _criacao = criacao;
        _daOrigem = daOrigem;
        _doDestino = doDestino;
        _origem = origem;
        _destino = destino;
        foreach (var resolvida in regras.Colunas)
        {
            _resolvidas[resolvida.Coluna] = resolvida;
        }
    }

    /// <summary>Monta o plano.</summary>
    /// <param name="configuracao">A configuração.</param>
    /// <param name="regras">As regras resolvidas.</param>
    /// <param name="registro">O registro, de onde sai a versão mais nova de cada transformador.</param>
    /// <param name="daOrigem">A comparação de nomes do banco da origem.</param>
    /// <param name="doDestino">A comparação de nomes do banco do destino.</param>
    /// <param name="origem">A leitura da origem, já conferida.</param>
    /// <param name="destino">A leitura do destino, já conferida.</param>
    /// <param name="gerarChave">O gerador da chave aleatória da sessão descartável.</param>
    /// <param name="cancelamento">Cancela o planejamento.</param>
    /// <returns>O plano montado.</returns>
    /// <exception cref="PlanoRecusadoException">Algum nome não se resolve, ou a configuração e o esquema não permitem copiar.</exception>
    /// <exception cref="OperationCanceledException">Cancelado pelo chamador durante a criação dos transformadores.</exception>
    internal static PlanoMontado Montar(
        ConfiguracaoIncognito configuracao,
        ResolucaoDeRegras regras,
        RegistroTransformadores registro,
        ComparadorDeNomes daOrigem,
        ComparadorDeNomes doDestino,
        LeituraDoBanco origem,
        LeituraDoBanco destino,
        Func<ChaveIncognito> gerarChave,
        CancellationToken cancelamento)
    {
        // A sessão descartável vive só durante a montagem: os transformadores são criados e descartados nela, e a chave
        // aleatória é zerada no fim, com erro ou sem.
        using var criacao = new CriacaoNoPlano(gerarChave, configuracao.Ambiente, regras.Dominios, cancelamento);
        var montagem = new MontagemDoPlano(configuracao, regras, registro, criacao, daOrigem, doDestino, origem, destino);
        montagem.Indexar();
        montagem.ResolverNomes();
        montagem._coletor.LancarSeHouverErros();
        montagem.ConferirUnidades();
        montagem._coletor.LancarSeHouverErros();
        return montagem.Publicar();
    }

    // P3: as unidades de cópia (toda tabela que não é partição) e o pareamento pelo dialeto do destino, com o nome da
    // origem entre aspas: mais de um candidato de um lado para o mesmo nome do outro é ambíguo. A partição vai à raiz.
    private void Indexar()
    {
        var doDestino = _destino.Esquema.Tabelas.Where(tabela => tabela.Tipo != TipoTabela.Particao).ToList();
        var pareadas = new Dictionary<TabelaEsquema, List<Unidade>>(ReferenceEqualityComparer.Instance);
        foreach (var tabela in _origem.Esquema.Tabelas.Where(tabela => tabela.Tipo != TipoTabela.Particao))
        {
            var unidade = new Unidade(tabela);
            _unidades.Add(unidade);
            var candidatas = doDestino
                .Select(outra => (Tabela: outra, Correspondencia: Comparar(_doDestino, tabela.Nome.Esquema, true, outra.Nome.Esquema, tabela.Nome.Nome, true, outra.Nome.Nome)))
                .Where(par => par.Correspondencia != CorrespondenciaNome.NaoCorresponde)
                .ToList();
            if (candidatas is [{ Correspondencia: CorrespondenciaNome.Corresponde } unica])
            {
                unidade.Destino = unica.Tabela;
                (pareadas.TryGetValue(unica.Tabela, out var lista) ? lista : pareadas[unica.Tabela] = []).Add(unidade);
            }
            else if (candidatas.Count > 0)
            {
                unidade.Ambigua = true;
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.NomeAmbiguo,
                    $"A tabela da origem corresponde a mais de uma tabela do destino, ou o dialeto do destino não sabe comparar os nomes sem adivinhar: {Lista(candidatas.Select(par => par.Tabela.Nome))}.",
                    unidade.NomeTabela));
            }
        }

        foreach (var (tabela, unidades) in pareadas.Where(par => par.Value.Count > 1))
        {
            foreach (var unidade in unidades)
            {
                unidade.Ambigua = true;
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.NomeAmbiguo,
                    $"A tabela {Exibir(tabela.Nome)} do destino corresponde a mais de uma tabela da origem: {Lista(unidades.Select(outra => outra.Origem!.Nome))}.",
                    unidade.NomeTabela));
            }
        }

        foreach (var tabela in doDestino.Where(tabela => !pareadas.ContainsKey(tabela)))
        {
            _unidades.Add(new Unidade(null) { Destino = tabela });
        }

        foreach (var unidade in _unidades)
        {
            if (unidade.Origem is { } daOrigem)
            {
                _unidadeDe[daOrigem] = unidade;
            }

            if (unidade.Destino is { } doLado && !unidade.Ambigua)
            {
                _unidadeDe[doLado] = unidade;
            }
        }

        MapearParticoes(_origem.Esquema);
        MapearParticoes(_destino.Esquema);
    }

    // Cada partição, também a intermediária, vai à unidade da raiz, subindo pelo pai.
    private void MapearParticoes(EsquemaBanco esquema)
    {
        var porNome = esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        foreach (var particao in esquema.Tabelas.Where(tabela => tabela.Tipo == TipoTabela.Particao))
        {
            var raiz = particao;
            while (raiz.Tipo == TipoTabela.Particao && raiz.Pai is { } pai && porNome.TryGetValue(pai, out var acima))
            {
                raiz = acima;
            }

            if (_unidadeDe.TryGetValue(raiz, out var unidade))
            {
                _unidadeDe[particao] = unidade;
            }
        }
    }

    // P4: cada entrada da configuração contra as tabelas dos dois lados, e as colunas e o derivarDe contra a origem.
    private void ResolverNomes()
    {
        foreach (var entrada in _configuracao.Tabelas)
        {
            ResolverEntrada(entrada);
        }
    }

    private void ResolverEntrada(RegraTabelaEscrita entrada)
    {
        var posicao = NoYaml(entrada.Posicao, entrada.Local);
        var escrito = Escrito(entrada.Nome);
        if (EsquemaDeSistema(entrada.Nome.Esquema))
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.EsquemaReservado,
                $"A entrada {escrito} está num esquema de sistema ou no esquema reservado ao Incognito: nenhuma tabela dele é copiada.",
                posicao: posicao));
            return;
        }

        var (naOrigem, origemIndeterminada) = Candidatas(_origem.Esquema, _daOrigem, entrada.Nome);
        var (noDestino, destinoIndeterminada) = Candidatas(_destino.Esquema, _doDestino, entrada.Nome);
        if (origemIndeterminada || destinoIndeterminada || naOrigem.Count > 1 || noDestino.Count > 1)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.NomeAmbiguo,
                $"A entrada {escrito} designa mais de uma tabela, ou o dialeto não sabe comparar o nome sem adivinhar: {Lista(naOrigem.Concat(noDestino).Select(tabela => tabela.Nome).Distinct())}.",
                posicao: posicao));
            return;
        }

        if (naOrigem.Count == 0 && noDestino.Count == 0)
        {
            _coletor.Erro(new ErroPlano(CodigosPlanejar.TabelaInexistente, $"A tabela {escrito} não existe na origem nem no destino.{DicaDeTabela(entrada.Nome)}", posicao: posicao));
            return;
        }

        var daOrigem = naOrigem.SingleOrDefault();
        var doDestino = noDestino.SingleOrDefault();

        // A partição, de um lado só ou dos dois, sempre leva à raiz: antes de conferir que os dois lados se correspondem.
        if (new[] { daOrigem, doDestino }.FirstOrDefault(tabela => tabela?.Tipo == TipoTabela.Particao) is { } particao)
        {
            var raiz = _unidadeDe.GetValueOrDefault(particao);
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.EntradaEmParticao,
                $"A entrada {escrito} designa a partição {Exibir(particao.Nome)}: a partição segue a raiz; escreva a regra {(raiz is null ? "na particionada" : $"em {raiz.NomeTabela}")}.",
                new NomeTabela(particao.Nome.Esquema, particao.Nome.Nome),
                posicao: posicao));
            return;
        }

        var unidade = _unidadeDe.GetValueOrDefault((daOrigem ?? doDestino)!);
        if (unidade is null || unidade.Ambigua)
        {
            // A tabela já ficou ambígua no pareamento: o erro saiu lá.
            return;
        }

        var naOutra = doDestino is null ? null : _unidadeDe.GetValueOrDefault(doDestino);
        if ((unidade.Origem is null) != (daOrigem is null) || (unidade.Destino is null) != (doDestino is null) || (doDestino is not null && !ReferenceEquals(naOutra, unidade)))
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.NomeAmbiguo,
                $"A entrada {escrito} não designa a mesma tabela nos dois lados: {Lista(naOrigem.Concat(noDestino).Select(tabela => tabela.Nome).Distinct())}.",
                unidade.NomeTabela,
                posicao: posicao));
            return;
        }

        if (_entradas.TryGetValue(unidade, out var anterior))
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.EntradaRepetida,
                $"A entrada {escrito} designa a mesma tabela que a entrada {Escrito(anterior.Nome)} (linha {anterior.Posicao.Linha.ToString(CultureInfo.InvariantCulture)}): junte as duas.",
                unidade.NomeTabela,
                posicao: posicao));
            return;
        }

        _entradas[unidade] = entrada;
        AvisarQuaseIguais(unidade);
        ResolverColunas(unidade, entrada);
    }

    private void ResolverColunas(Unidade unidade, RegraTabelaEscrita entrada)
    {
        if (unidade.Origem is not { } tabela)
        {
            return;
        }

        var nome = unidade.NomeTabela;
        var vistas = new Dictionary<ColunaEsquema, RegraColunaEscrita>(ReferenceEqualityComparer.Instance);
        foreach (var regra in entrada.Colunas)
        {
            var posicao = NoYaml(regra.Posicao, regra.Local);
            var (coluna, falha) = Coluna(tabela, regra.Coluna);
            switch (falha)
            {
                case FalhaDeNome.Nenhuma:
                    _coletor.Erro(new ErroPlano(CodigosPlanejar.ColunaInexistente, $"A coluna {Escrito(regra.Coluna)} não existe na tabela da origem.{DicaDeColuna(tabela, regra.Coluna)}", nome, posicao: posicao));
                    continue;
                case FalhaDeNome.Ambigua:
                    _coletor.Erro(new ErroPlano(CodigosPlanejar.NomeAmbiguo, $"A coluna {Escrito(regra.Coluna)} designa mais de uma coluna, ou o dialeto não sabe comparar o nome sem adivinhar.", nome, posicao: posicao));
                    continue;
            }

            var posicaoDaColuna = Indice(tabela, coluna!);
            if (vistas.TryGetValue(coluna!, out var outra))
            {
                _coletor.Erro(
                    new ErroPlano(CodigosPlanejar.EntradaRepetida, $"A coluna {Escrito(regra.Coluna)} designa a mesma coluna que {Escrito(outra.Coluna)}: junte as duas.", nome, coluna!.Nome, posicao: posicao),
                    posicaoDaColuna);
                continue;
            }

            vistas[coluna!] = regra;
            _colunas[regra] = coluna!;
            AvisarQuaseIguais(unidade, coluna!);
            if (regra.DerivarDe is { } derivarDe)
            {
                var (derivada, falhaDaDerivada) = Coluna(tabela, derivarDe);
                if (falhaDaDerivada == FalhaDeNome.Nenhuma)
                {
                    _coletor.Erro(
                        new ErroPlano(CodigosPlanejar.DerivarDeInexistente, $"A coluna {Escrito(derivarDe)} de 'derivarDe' não existe na tabela da origem.{DicaDeColuna(tabela, derivarDe)}", nome, coluna!.Nome, posicao: posicao),
                        posicaoDaColuna);
                }
                else if (falhaDaDerivada == FalhaDeNome.Ambigua)
                {
                    _coletor.Erro(
                        new ErroPlano(CodigosPlanejar.NomeAmbiguo, $"A coluna {Escrito(derivarDe)} de 'derivarDe' designa mais de uma coluna, ou o dialeto não sabe comparar o nome sem adivinhar.", nome, coluna!.Nome, posicao: posicao),
                        posicaoDaColuna);
                }
                else
                {
                    _derivadas[regra] = derivada!;
                }
            }
        }

        ConferirCiclos(unidade, tabela, vistas);
    }

    // O ciclo de derivarDe sobre os nomes resolvidos: a leitura do YAML compara o que foi escrito, e "B" e "b" sem aspas
    // são a mesma coluna.
    private void ConferirCiclos(Unidade unidade, TabelaEsquema tabela, Dictionary<ColunaEsquema, RegraColunaEscrita> regras)
    {
        var emCiclo = new HashSet<RegraColunaEscrita>(ReferenceEqualityComparer.Instance);
        foreach (var inicio in regras.Values)
        {
            var caminho = new List<RegraColunaEscrita>();
            var atual = inicio;
            while (atual is not null && !emCiclo.Contains(atual))
            {
                var repetida = caminho.FindIndex(regra => ReferenceEquals(regra, atual));
                if (repetida >= 0)
                {
                    emCiclo.UnionWith(caminho.Skip(repetida));
                    break;
                }

                caminho.Add(atual);
                atual = _derivadas.TryGetValue(atual, out var derivada) ? regras.GetValueOrDefault(derivada) : null;
            }
        }

        foreach (var regra in emCiclo)
        {
            _coletor.Erro(
                new ErroPlano(
                    CodigosPlanejar.DerivarDeEmCiclo,
                    "'derivarDe' em ciclo depois de resolvidos os nomes: a coluna depende, direta ou indiretamente, de si mesma.",
                    unidade.NomeTabela,
                    _colunas[regra].Nome,
                    posicao: NoYaml(regra.Posicao, regra.Local)),
                Indice(tabela, _colunas[regra]));
        }
    }

    // P5: a ação de cada unidade, as estrangeiras, o que só existe num lado, o esquema reservado e a origem contra o destino.
    private void ConferirUnidades()
    {
        foreach (var unidade in _unidades)
        {
            _entradas.TryGetValue(unidade, out var entrada);
            var estrangeiraNaOrigem = unidade.Origem?.Estrangeira == true;
            var estrangeiraNoDestino = unidade.Destino?.Estrangeira == true;
            var nome = unidade.NomeTabela;
            var (acao, escrita) = entrada is { AcaoEscrita: true } ? (entrada.Acao, true) : (AcaoTabela.Copiar, false);
            var comColunas = entrada is { Colunas.Count: > 0 };

            // Fora da configuração (sem ação e sem regra de coluna), só a estrangeira dos dois lados é ignorada sozinha: com
            // um lado local, ignorar deixaria intocado o dado do destino, e copiar tocaria o lado de fora do banco.
            if (!escrita && !comColunas && unidade.Origem is not null && unidade.Destino is not null)
            {
                if (estrangeiraNaOrigem && estrangeiraNoDestino)
                {
                    acao = AcaoTabela.Ignorar;
                    _coletor.Aviso(new Aviso(CodigosPlanejar.EstrangeiraIgnorada, "Tabela estrangeira nos dois lados e fora da configuração: os dados ficam fora do banco, e ela é ignorada.", nome));
                }
                else if (estrangeiraNaOrigem || estrangeiraNoDestino)
                {
                    _coletor.Erro(new ErroPlano(
                        CodigosPlanejar.EstrangeiraNumLado,
                        $"A tabela é estrangeira {(estrangeiraNaOrigem ? "na origem" : "no destino")} e local {(estrangeiraNaOrigem ? "no destino" : "na origem")}: copiar tocaria os dados fora do banco, e ignorar deixaria o dado do destino intocado; escreva 'acao: ignorar'{(estrangeiraNaOrigem ? " ou 'acao: esvaziar'" : "")}.",
                        nome,
                        posicao: entrada is null ? null : NoYaml(entrada.Posicao, entrada.Local)));
                }
            }

            _acoes[unidade] = (acao, escrita);

            // A ação só é recusada pelo lado que ela toca: copiar lê a origem e grava o destino; esvaziar só grava o destino.
            var tocaEstrangeira = acao switch
            {
                AcaoTabela.Copiar => estrangeiraNaOrigem || estrangeiraNoDestino,
                AcaoTabela.Esvaziar => estrangeiraNoDestino,
                _ => false,
            };
            if (entrada is not null && (escrita || comColunas) && tocaEstrangeira)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.EstrangeiraTocada,
                    "Tabela estrangeira no lado que a ação toca (copiar lê a origem e grava o destino; esvaziar só grava o destino), ou com regra de coluna: os dados ficam fora do banco, e o Incognito nunca os toca; use 'acao: ignorar', sem regras de coluna.",
                    nome,
                    posicao: NoYaml(entrada.Posicao, entrada.Local)));
            }

            if (acao != AcaoTabela.Ignorar && TemParticaoEstrangeira(unidade))
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.ParticaoEstrangeira,
                    "Particionada com partição estrangeira: esvaziar e copiar pela raiz escreveriam fora do banco; use 'acao: ignorar', sem regras de coluna.",
                    nome,
                    posicao: entrada is null ? null : NoYaml(entrada.Posicao, entrada.Local)));
            }

            if (unidade.Destino is null && acao != AcaoTabela.Ignorar)
            {
                _coletor.Erro(new ErroPlano(CodigosPlanejar.SoNaOrigem, "A tabela existe na origem e não no destino: crie-a no destino ou use 'acao: ignorar'.", nome));
            }

            if (unidade.Origem is null && !(escrita && acao is AcaoTabela.Esvaziar or AcaoTabela.Ignorar))
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.SoNoDestino,
                    "A tabela existe no destino e não na origem: o dado dela nunca seria tocado; escreva 'acao: esvaziar' ou 'acao: ignorar'.",
                    nome));
            }

            if (acao == AcaoTabela.Copiar && unidade.Origem is { } daOrigem && unidade.Destino is { } doDestino)
            {
                CompararColunas(unidade, entrada, daOrigem, doDestino);
                CompararRestricoes(nome, daOrigem, doDestino);
            }
        }

        ConferirReservado(_origem.Esquema, "na origem");
        ConferirReservado(_destino.Esquema, "no destino");
    }

    private bool TemParticaoEstrangeira(Unidade unidade) =>
        _origem.Esquema.Tabelas.Concat(_destino.Esquema.Tabelas)
            .Any(tabela => tabela.Tipo == TipoTabela.Particao && tabela.Estrangeira && ReferenceEquals(_unidadeDe.GetValueOrDefault(tabela), unidade));

    private void ConferirReservado(EsquemaBanco esquema, string lado)
    {
        if (esquema.ObjetosNoEsquemaReservado is not { } objetos)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.ObjetoNoEsquemaReservado,
                $"O dialeto não informa o que há {lado} no esquema reservado ao Incognito: sem isso, o plano não aprova a cópia."));
            return;
        }

        // Também o que o dialeto deixou entre as tabelas, sequences e objetos residuais: copiado, sobrescreveria o diário.
        var copiaveis = esquema.Tabelas.Select(tabela => tabela.Nome)
            .Concat(esquema.Sequences.Select(sequence => sequence.Nome))
            .Concat(esquema.ObjetosResiduais.Select(residual => residual.Nome))
            .Where(nome => string.Equals(nome.Esquema, Reservado, StringComparison.Ordinal));
        var acao = lado == "na origem" ? "renomeie o esquema na origem ou leia de uma cópia sem ele" : "remova o esquema do destino antes de copiar";
        foreach (var objeto in objetos.Concat(copiaveis).Distinct())
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.ObjetoNoEsquemaReservado,
                $"Há {Exibir(objeto)} {lado}, no esquema reservado ao Incognito: {acao}.",
                objeto: objeto.Nome));
        }
    }

    // As colunas da origem contra as do destino, numa unidade copiada.
    private void CompararColunas(Unidade unidade, RegraTabelaEscrita? entrada, TabelaEsquema origem, TabelaEsquema destino)
    {
        var nome = unidade.NomeTabela;
        var tratadas = new Dictionary<ColunaEsquema, RegraColunaEscrita>(ReferenceEqualityComparer.Instance);
        foreach (var regra in entrada?.Colunas.Where(regra => regra.Tipo != TipoRegraColuna.Manter && _colunas.ContainsKey(regra)) ?? [])
        {
            tratadas[_colunas[regra]] = regra;
        }

        var candidatasPorColuna = origem.Colunas
            .Select(coluna => destino.Colunas
                .Select(outra => (Coluna: outra, Correspondencia: _doDestino(coluna.Nome, true, outra.Nome)))
                .Where(par => par.Correspondencia != CorrespondenciaNome.NaoCorresponde)
                .ToList())
            .ToList();
        var paresNoDestino = new Dictionary<ColunaEsquema, int>(ReferenceEqualityComparer.Instance);
        foreach (var candidatas in candidatasPorColuna.Where(candidatas => candidatas is [{ Correspondencia: CorrespondenciaNome.Corresponde }]))
        {
            paresNoDestino[candidatas[0].Coluna] = paresNoDestino.GetValueOrDefault(candidatas[0].Coluna) + 1;
        }
        var pareadas = new HashSet<ColunaEsquema>(ReferenceEqualityComparer.Instance);
        for (var indice = 0; indice < origem.Colunas.Count; indice++)
        {
            var coluna = origem.Colunas[indice];
            var candidatas = candidatasPorColuna[indice];
            if (candidatas is not [{ Correspondencia: CorrespondenciaNome.Corresponde } unica] || paresNoDestino[unica.Coluna] > 1)
            {
                if (candidatas.Count == 0 && coluna.Geracao != GeracaoColuna.Nenhuma && tratadas.TryGetValue(coluna, out var naGerada))
                {
                    // A gerada sem par não é lida nem gravada, mas a regra escrita nela continua um engano.
                    _coletor.Erro(new ErroPlano(CodigosPlanejar.RegraEmGerada, MensagemDaGerada("na origem"), nome, coluna.Nome, posicao: NoYaml(naGerada.Posicao, naGerada.Local)), indice);
                }
                else if (coluna.Geracao == GeracaoColuna.Nenhuma || candidatas.Count > 0)
                {
                    _coletor.Erro(
                        candidatas.Count == 0
                            ? new ErroPlano(CodigosPlanejar.ColunaAusenteNoDestino, "A coluna existe na origem e não no destino: crie-a no destino.", nome, coluna.Nome)
                            : new ErroPlano(
                                CodigosPlanejar.NomeAmbiguo,
                                "A coluna da origem não tem um par único no destino (mais de uma coluna corresponde, outra coluna da origem corresponde à mesma, ou o dialeto do destino não sabe comparar os nomes sem adivinhar).",
                                nome,
                                coluna.Nome),
                        indice);
                }

                continue;
            }

            pareadas.Add(unica.Coluna);
            if (tratadas.TryGetValue(coluna, out var tratada))
            {
                if (coluna.Tipo != unica.Coluna.Tipo || coluna.Situacao != unica.Coluna.Situacao)
                {
                    _coletor.Erro(
                        new ErroPlano(
                            CodigosPlanejar.TratadaComTipoDiferente,
                            $"A coluna tratada tem tipo {Tipo(coluna)} na origem e {Tipo(unica.Coluna)} no destino: o transformador é criado para um tipo só.",
                            nome,
                            coluna.Nome),
                        indice);
                }
                else
                {
                    ConferirTratada(unidade, tratada, coluna, unica.Coluna, destino, indice);
                }
            }
            else if (coluna.Tipo != unica.Coluna.Tipo)
            {
                _coletor.Aviso(
                    new Aviso(CodigosPlanejar.MaisEstreitaNoDestino, $"A coluna tem tipo {Tipo(coluna)} na origem e {Tipo(unica.Coluna)} no destino: a cópia depende dos dados.", nome, coluna.Nome),
                    indice);
            }
            else if (MaisEstreita(coluna, unica.Coluna))
            {
                _coletor.Aviso(
                    new Aviso(CodigosPlanejar.MaisEstreitaNoDestino, "A coluna é mais estreita no destino (comprimento, faixa ou precisão): a cópia depende dos dados.", nome, coluna.Nome),
                    indice);
            }

            if (!MesmaColacao(coluna, unica.Coluna))
            {
                _coletor.Aviso(
                    new Aviso(CodigosPlanejar.ColacaoDiferente, "A colação da coluna se comporta diferente na origem e no destino: ordem e igualdade dos textos podem mudar.", nome, coluna.Nome),
                    indice);
            }
        }

        for (var indice = 0; indice < destino.Colunas.Count; indice++)
        {
            var coluna = destino.Colunas[indice];
            if (!pareadas.Contains(coluna) && !coluna.Anulavel && coluna.Identidade == IdentidadeColuna.Nenhuma && coluna.Geracao == GeracaoColuna.Nenhuma)
            {
                _coletor.Aviso(
                    new Aviso(CodigosPlanejar.ObrigatoriaSoNoDestino, "A coluna só existe no destino e não aceita nulo: sem um valor padrão no banco, a cópia falha.", nome, coluna.Nome),
                    origem.Colunas.Count + indice);
            }
        }
    }

    // P6: uma coluna tratada, com o mesmo tipo e situação nos dois lados. Primeiro os avisos da regra; depois a gerada, a
    // forma canônica e o tipo, que barram o resto; os nulos; e os limites, que dão o contexto com que o transformador é
    // criado, lido e descartado na sessão descartável.
    private void ConferirTratada(Unidade unidade, RegraColunaEscrita regra, ColunaEsquema origem, ColunaEsquema destino, TabelaEsquema tabelaDoDestino, int indice)
    {
        var resolvida = _resolvidas[regra];
        var fabrica = resolvida.Fabrica;
        var nome = unidade.NomeTabela;
        var posicao = NoYaml(regra.Posicao, regra.Local);
        void Erro(string codigo, string mensagem, PosicaoConfiguracao? onde) =>
            _coletor.Erro(new ErroPlano(codigo, mensagem, nome, origem.Nome, posicao: onde ?? posicao), indice);

        AvisarVersaoMaisNova(regra, fabrica, nome, origem.Nome, posicao, indice);
        if (PoliticasQueAnulam.UsaEntradaMalformada(fabrica) && resolvida.Regra.EntradaMalformada == PoliticaEntradaMalformada.Preservar)
        {
            var (onde, deOnde) = OndeEstaAPolitica(regra, resolvida, new PoliticaQueAnula("entradaMalformada: preservar", null, null));
            _coletor.Aviso(
                new Aviso(CodigosPlanejar.MalformadaPreservada, $"'entradaMalformada: preservar' ({deOnde}) copia em claro a entrada fora do formato.", nome, origem.Nome, posicao: onde ?? posicao),
                indice);
        }

        if (origem.Geracao != GeracaoColuna.Nenhuma || destino.Geracao != GeracaoColuna.Nenhuma)
        {
            Erro(CodigosPlanejar.RegraEmGerada, MensagemDaGerada(origem.Geracao != GeracaoColuna.Nenhuma ? "na origem" : "no destino"), null);
            return;
        }

        // O ponto único do tipo sem forma canônica: hoje recusa sempre, porque nulo e fixo ainda não existem.
        if (destino.Tipo is not { } tipo || destino.Situacao != SituacaoTipo.Suportado)
        {
            var situacao = destino.Situacao switch
            {
                SituacaoTipo.ComRessalva => "aceito com ressalva, como json, xml, enum, domínio sem forma canônica ou array",
                SituacaoTipo.SuportadoPorExclusao => "suportado só para copiar",
                SituacaoTipo.NaoSuportado => "não suportado",
                _ => "sem representação para o transformador",
            };
            Erro(
                CodigosPlanejar.SemFormaCanonica,
                $"O tipo da coluna no destino ({situacao}) não chega a um transformador: o {fabrica.Nome}@{fabrica.Versao} não recebe o valor dela. Use 'acao: manter' (com 'justificativa', se a coluna for sensível) ou esvazie a tabela.",
                null);
            return;
        }

        if (!fabrica.TiposSuportados.Contains(tipo))
        {
            Erro(CodigosPlanejar.TipoNaoAceito, $"O transformador {fabrica.Nome}@{fabrica.Versao} não aceita coluna do tipo {tipo} (aceita {string.Join(", ", fabrica.TiposSuportados.Order())}).", null);
            return;
        }

        var naChave = tabelaDoDestino.ChavePrimaria.Contains(destino.Nome, StringComparer.Ordinal);
        if (!destino.Anulavel || naChave)
        {
            var porque = naChave ? "é da chave primária no destino" : "não aceita NULL no destino";
            foreach (var politica in PoliticasQueAnulam.Da(resolvida, _regras.Dominios))
            {
                var (onde, deOnde) = OndeEstaAPolitica(regra, resolvida, politica);
                var (citado, saida) = politica.Dominio is null
                    ? ("", "troque a política onde ela está escrita, ou escreva outra na coluna")
                    : ($" do domínio {politica.Dominio}, citado em '{politica.Parametro}'", "troque a política do domínio citado, ou cite outro domínio");
                Erro(
                    CodigosPlanejar.NuloEmObrigatoria,
                    $"A regra produz NULL com '{politica.Politica}'{citado} ({deOnde}), e a coluna {porque}: a gravação falharia; {saida}.",
                    onde);
            }
        }

        if (Contexto(regra, destino, tipo, Erro) is not { } contexto)
        {
            return;
        }

        _contextos[regra] = contexto;
        if (_criacao.Criar(fabrica, resolvida.Regra, contexto) is { } falha)
        {
            Erro(falha.Codigo, falha.Mensagem, null);
        }
    }

    // O contexto de criação pela coluna do destino; nulo quando o dialeto não mediu o que o contexto precisa. Em texto, o
    // limite vira unidades UTF-16 que cabem com certeza: em bytes, divide pelo maior caractere da codificação (sem as
    // propriedades do banco, 4, o pior caso); em binário, vai em bytes.
    private ContextoDaColuna? Contexto(RegraColunaEscrita regra, ColunaEsquema destino, TipoColuna tipo, Action<string, string, PosicaoConfiguracao?> erro)
    {
        int? comprimento = null;
        if (tipo is TipoColuna.Texto or TipoColuna.Binario)
        {
            if (destino.Comprimento is not { } limite)
            {
                erro(
                    CodigosPlanejar.ComprimentoNaoMedido,
                    "O dialeto não mediu o comprimento da coluna no destino: sem ele, o pseudônimo pode não caber. Atualize o dialeto, ou use 'acao: manter' na coluna.",
                    null);
                return null;
            }

            var bytesPorCaractere = _destino.Esquema.Propriedades?.Codificacao.BytesPorCaractereMaximo ?? 4;
            comprimento = limite.Valor is not { } valor ? null
                : tipo == TipoColuna.Binario || limite.Unidade != UnidadeComprimento.Bytes ? valor
                : valor / bytesPorCaractere;
            if (comprimento == 0)
            {
                erro(
                    CodigosPlanejar.LimiteSemCaractere,
                    $"O limite da coluna no destino, {limite.Valor!.Value.ToString(CultureInfo.InvariantCulture)} bytes, não comporta um caractere de até {bytesPorCaractere.ToString(CultureInfo.InvariantCulture)} bytes da codificação dele.",
                    null);
                return null;
            }
        }

        long? menor = null;
        long? maior = null;
        if (tipo == TipoColuna.Inteiro)
        {
            if (destino.Faixa is not { } faixa)
            {
                erro(
                    CodigosPlanejar.FaixaOuPrecisaoNaoMedida,
                    "O dialeto não informou a faixa da coluna inteira no destino: sem ela, o pseudônimo pode não caber. Atualize o dialeto, ou use 'acao: manter' na coluna.",
                    null);
                return null;
            }

            (menor, maior) = (faixa.Minimo, faixa.Maximo);
        }

        int? precisao = null;
        int? escala = null;
        if (tipo == TipoColuna.Decimal)
        {
            // O decimal do .NET guarda até 28 dígitos: a precisão, a escala e os dígitos da parte inteira (precisão menos escala;
            // com escala negativa, mais que a precisão) precisam caber.
            var escalaMedida = destino.Escala ?? 0;
            if (destino.Precisao is not { } medida || medida > MaiorPrecisao || escalaMedida > MaiorPrecisao || medida - escalaMedida > MaiorPrecisao)
            {
                erro(
                    CodigosPlanejar.FaixaOuPrecisaoNaoMedida,
                    destino.Precisao is null
                        ? "A coluna decimal não declara a precisão no destino (ou o dialeto não a informou): sem ela, o pseudônimo pode não caber. Declare a precisão, ou use 'acao: manter' na coluna."
                        : $"A coluna decimal passa de {MaiorPrecisao.ToString(CultureInfo.InvariantCulture)} dígitos no destino (na precisão, na escala ou na parte inteira), o limite do decimal do .NET; use 'acao: manter' na coluna.",
                    null);
                return null;
            }

            (precisao, escala) = (medida, destino.Escala);
        }

        return new ContextoDaColuna(tipo, comprimento, menor, maior, precisao, escala, destino.Colacao?.Nome, _derivadas.GetValueOrDefault(regra)?.Tipo);
    }

    private static string MensagemDaGerada(string lado) =>
        $"A coluna é gerada {lado}: o banco recalcula o valor dela, e a regra não se aplica; tire a regra e trate as colunas de que ela é calculada.";

    // A versão mais nova de um transformador: na regra que usa um domínio declarado, um aviso só, na declaração (a correção
    // é lá); na escrita direto na coluna, um por coluna.
    private void AvisarVersaoMaisNova(RegraColunaEscrita regra, FabricaRegistrada fabrica, NomeTabela tabela, string coluna, PosicaoConfiguracao posicao, int indice)
    {
        if (_registro.VersaoMaisRecente(fabrica.Nome) is not { } recente || recente <= fabrica.Versao)
        {
            return;
        }

        var mensagem = $"Há versão mais nova do transformador {fabrica.Nome} (@{recente.ToString(CultureInfo.InvariantCulture)}); a regra usa a @{fabrica.Versao.ToString(CultureInfo.InvariantCulture)}.";
        if (regra.Transformador is null && regra.Dominio is { } dominio && _configuracao.Dominios.FirstOrDefault(declarado => declarado.Nome == dominio) is { } declarado)
        {
            _coletor.Aviso(new Aviso(CodigosPlanejar.VersaoMaisNova, $"{mensagem} Ela vem do domínio {dominio}.", posicao: NoYaml(declarado.Posicao, declarado.Local)));
            return;
        }

        _coletor.Aviso(new Aviso(CodigosPlanejar.VersaoMaisNova, mensagem, tabela, coluna, posicao: posicao), indice);
    }

    // De onde vem a política: escrita na coluna, na declaração do domínio, ou na raiz (que não guarda posição; aí o erro
    // aponta a coluna). Um domínio declarado numa coluna (o do id.permutar) ainda não existe.
    private (PosicaoConfiguracao? Onde, string DeOnde) OndeEstaAPolitica(RegraColunaEscrita regra, ColunaResolvida resolvida, PoliticaQueAnula politica)
    {
        var invalida = politica.Politica.StartsWith("entradaInvalida", StringComparison.Ordinal);
        bool Escrita(PoliticasDeEntrada politicas) => invalida ? politicas.EntradaInvalida is not null : politicas.EntradaMalformada is not null;

        if (politica.Dominio is null && Escrita(regra.Politicas))
        {
            return (null, "escrita na coluna");
        }

        var dominio = politica.Dominio ?? resolvida.Regra.Dominio.Nome;
        if (_configuracao.Dominios.FirstOrDefault(declarado => declarado.Nome == dominio) is { } declarado && Escrita(declarado.Politicas))
        {
            return (NoYaml(declarado.Posicao, declarado.Local), $"escrita no domínio {dominio}");
        }

        return (null, "da política global, em 'politica'");
    }

    // A presença de cada restrição nos dois lados, pelo nome (a chave primária, pela existência).
    private void CompararRestricoes(NomeTabela nome, TabelaEsquema origem, TabelaEsquema destino)
    {
        if ((origem.ChavePrimaria.Count > 0) != (destino.ChavePrimaria.Count > 0))
        {
            var (comChave, lado) = origem.ChavePrimaria.Count > 0 ? (origem, "na origem") : (destino, "no destino");
            var indice = comChave.IndicesUnicos.FirstOrDefault(indice => indice.Primario)?.Nome;
            _coletor.Aviso(new Aviso(
                CodigosPlanejar.RestricaoSoNumLado,
                $"A chave primária{(indice is null ? "" : " " + NomeTabela.Parte(indice))} só existe {lado}.",
                nome,
                objeto: indice));
        }

        AvisarSoNumLado(nome, "A chave estrangeira", origem.ChavesEstrangeiras.Select(chave => chave.Nome), destino.ChavesEstrangeiras.Select(chave => chave.Nome));
        AvisarSoNumLado(
            nome,
            "O índice único",
            origem.IndicesUnicos.Where(indice => !indice.Primario).Select(indice => indice.Nome),
            destino.IndicesUnicos.Where(indice => !indice.Primario).Select(indice => indice.Nome));
        AvisarSoNumLado(nome, "O CHECK", origem.RestricoesCheck.Select(check => check.Nome), destino.RestricoesCheck.Select(check => check.Nome));
    }

    // O tipo vem com o artigo, para a mensagem concordar.
    private void AvisarSoNumLado(NomeTabela nome, string tipo, IEnumerable<string> daOrigem, IEnumerable<string> doDestino)
    {
        var origem = daOrigem.ToHashSet(StringComparer.Ordinal);
        var destino = doDestino.ToHashSet(StringComparer.Ordinal);
        foreach (var (objeto, lado) in origem.Except(destino).Select(objeto => (objeto, "na origem")).Concat(destino.Except(origem).Select(objeto => (objeto, "no destino"))))
        {
            _coletor.Aviso(new Aviso(CodigosPlanejar.RestricaoSoNumLado, $"{tipo} {NomeTabela.Parte(objeto)} só existe {lado}.", nome, objeto: objeto));
        }
    }

    // O plano: as unidades em ordem, cada coluna da origem com a regra escrita e a classificação, e a carga.
    private PlanoMontado Publicar()
    {
        var tabelas = new List<MapaTabela>();
        var unidades = new List<UnidadeDeCopia>();
        foreach (var unidade in _unidades.OrderBy(unidade => unidade.NomeTabela, Comparer<NomeTabela>.Create(ColetorDoPlano.CompararTabelas)))
        {
            var (acao, escrita) = _acoes[unidade];
            _entradas.TryGetValue(unidade, out var entrada);
            var regras = new Dictionary<ColunaEsquema, RegraColunaEscrita>(ReferenceEqualityComparer.Instance);
            foreach (var regra in entrada?.Colunas ?? [])
            {
                if (_colunas.TryGetValue(regra, out var resolvida))
                {
                    regras[resolvida] = regra;
                }
            }

            var tabela = unidade.Origem;
            HashSet<string> identificadores = tabela is null ? [] : Identificadores(tabela, _origem.Esquema);
            var colunas = tabela?.Colunas.Select(coluna =>
            {
                var regra = regras.GetValueOrDefault(coluna);
                return new MapaColuna(coluna.Nome, coluna.Tipo, regra is null ? null : Regra(regra), new Classificacao(regra?.Sensivel == true, identificadores.Contains(coluna.Nome)));
            }).ToArray() ?? [];
            tabelas.Add(new MapaTabela(unidade.NomeTabela, acao, escrita, colunas, entrada is null ? null : NoYaml(entrada.Posicao, entrada.Local)));

            var daOrigem = tabela is null ? default((bool SoAPropria, bool AdmiteFreeze, IReadOnlyList<string> ColunasLidas)?) : UnidadeDeCopia.Forma(tabela, _origem.Esquema);
            var doDestino = unidade.Destino is null ? default((bool SoAPropria, bool AdmiteFreeze, IReadOnlyList<string> ColunasLidas)?) : UnidadeDeCopia.Forma(unidade.Destino, _destino.Esquema);
            var soAPropria = daOrigem?.SoAPropria == true || doDestino?.SoAPropria == true;
            var admiteFreeze = (doDestino ?? daOrigem)!.Value.AdmiteFreeze;
            var tratadas = tabela?.Colunas
                .Where(coluna => regras.GetValueOrDefault(coluna) is { } regra && _resolvidas.ContainsKey(regra))
                .Select(coluna =>
                {
                    var resolvida = _resolvidas[regras[coluna]];

                    // Sem o contexto, o executor não recriaria o transformador como o planejar o conferiu: é defeito.
                    var contexto = _contextos.TryGetValue(regras[coluna], out var criado) ? criado : throw new InvalidOperationException("Coluna tratada sem o contexto de criação.");
                    return new ColunaTratada(coluna.Nome, resolvida.Fabrica, resolvida.Regra, resolvida.Normalizados, contexto);
                })
                .ToArray() ?? [];
            unidades.Add(new UnidadeDeCopia(unidade.NomeTabela, acao, soAPropria, admiteFreeze, daOrigem?.ColunasLidas ?? [], tratadas));
        }

        var carga = new DadosDeExecucao(ConfiguracaoDaExecucao.De(_configuracao), _origem, _destino, unidades, _regras.Dominios);
        return new PlanoMontado(tabelas, _coletor.Avisos(), carga);
    }

    // A regra escrita como regra efetiva (sem propagação ainda): manter, ou o transformador resolvido com o domínio e as
    // políticas efetivas.
    private RegraColuna Regra(RegraColunaEscrita regra)
    {
        var posicao = NoYaml(regra.Posicao, regra.Local);
        if (!_resolvidas.TryGetValue(regra, out var resolvida))
        {
            // A resolução recusa antes do banco qualquer regra que não resolve: sem ela, a coluna sairia em claro.
            return regra.Tipo == TipoRegraColuna.Manter
                ? new RegraColuna(TipoRegra.Manter, null, null, null, null, null, null, null, regra.Origem, null, null, posicao)
                : throw new InvalidOperationException("Regra de transformação sem resolução.");
        }

        return new RegraColuna(
            TipoRegra.Transformar,
            resolvida.Fabrica.Nome,
            resolvida.Fabrica.Versao,
            resolvida.Regra.Dominio,
            resolvida.Fabrica.Injetividade,
            resolvida.Regra.EntradaInvalida,
            resolvida.Regra.EntradaMalformada,
            _derivadas.GetValueOrDefault(regra)?.Nome,
            regra.Origem,
            null,
            null,
            posicao);
    }

    // As colunas que identificam linhas: as da chave primária, as das chaves estrangeiras (dos dois lados) e as dos índices
    // únicos, também as citadas pelas expressões deles.
    private static HashSet<string> Identificadores(TabelaEsquema tabela, EsquemaBanco esquema) =>
    [
        .. tabela.ChavePrimaria,
        .. tabela.ChavesEstrangeiras.SelectMany(chave => chave.Colunas),
        .. tabela.IndicesUnicos.SelectMany(indice => indice.Elementos).Select(elemento => elemento.Coluna).OfType<string>(),
        .. tabela.IndicesUnicos.SelectMany(indice => indice.ReferenciasDasExpressoes?.Colunas ?? []),
        .. esquema.Tabelas.SelectMany(outra => outra.ChavesEstrangeiras).Where(chave => chave.Referenciada.Equals(tabela.Nome)).SelectMany(chave => chave.ColunasReferenciadas),
    ];

    private static (List<TabelaEsquema> Tabelas, bool Indeterminada) Candidatas(EsquemaBanco esquema, ComparadorDeNomes comparar, NomeTabelaEscrito nome)
    {
        var candidatas = new List<TabelaEsquema>();
        var indeterminada = false;
        foreach (var tabela in esquema.Tabelas)
        {
            var correspondencia = Comparar(comparar, nome.Esquema.Nome, nome.Esquema.EntreAspas, tabela.Nome.Esquema, nome.Tabela.Nome, nome.Tabela.EntreAspas, tabela.Nome.Nome);
            if (correspondencia != CorrespondenciaNome.NaoCorresponde)
            {
                candidatas.Add(tabela);
                indeterminada |= correspondencia == CorrespondenciaNome.Indeterminada;
            }
        }

        return (candidatas, indeterminada);
    }

    private (ColunaEsquema? Coluna, FalhaDeNome Falha) Coluna(TabelaEsquema tabela, Identificador nome)
    {
        var candidatas = tabela.Colunas
            .Select(coluna => (Coluna: coluna, Correspondencia: _daOrigem(nome.Nome, nome.EntreAspas, coluna.Nome)))
            .Where(par => par.Correspondencia != CorrespondenciaNome.NaoCorresponde)
            .ToList();
        return candidatas switch
        {
            [] => (null, FalhaDeNome.Nenhuma),
            [{ Correspondencia: CorrespondenciaNome.Corresponde } unica] => (unica.Coluna, FalhaDeNome.Sem),
            _ => (null, FalhaDeNome.Ambigua),
        };
    }

    // Esquema e nome: não corresponde se uma parte não corresponde; indeterminada se alguma é.
    private static CorrespondenciaNome Comparar(
        ComparadorDeNomes comparar, string esquemaEscrito, bool esquemaEntreAspas, string esquema, string nomeEscrito, bool nomeEntreAspas, string nome)
    {
        var doEsquema = comparar(esquemaEscrito, esquemaEntreAspas, esquema);
        if (doEsquema == CorrespondenciaNome.NaoCorresponde)
        {
            return CorrespondenciaNome.NaoCorresponde;
        }

        var doNome = comparar(nomeEscrito, nomeEntreAspas, nome);
        return doNome == CorrespondenciaNome.NaoCorresponde ? CorrespondenciaNome.NaoCorresponde
            : doEsquema == CorrespondenciaNome.Indeterminada || doNome == CorrespondenciaNome.Indeterminada ? CorrespondenciaNome.Indeterminada
            : CorrespondenciaNome.Corresponde;
    }

    // O esquema reservado e os de sistema, pela comparação da origem (o indeterminado segue e cai no ambíguo); os prefixados
    // com pg_ são do PostgreSQL.
    private bool EsquemaDeSistema(Identificador esquema) =>
        _sistema.Append(Reservado).Any(nome => _daOrigem(esquema.Nome, esquema.EntreAspas, nome) == CorrespondenciaNome.Corresponde)
        || esquema.Nome.StartsWith("pg_", esquema.EntreAspas ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    // Outro objeto que difere do resolvido só pela caixa: a entrada pode designar o errado.
    private void AvisarQuaseIguais(Unidade unidade)
    {
        var nome = unidade.Nome;
        foreach (var outro in _origem.Esquema.Tabelas.Concat(_destino.Esquema.Tabelas).Select(tabela => tabela.Nome).Where(outro => QuaseIgual(nome, outro)).Distinct())
        {
            _coletor.Aviso(new Aviso(
                CodigosPlanejar.NomeQuaseIgual,
                $"A tabela {Exibir(outro)} difere desta só na caixa: confira se a entrada designa a tabela certa.",
                unidade.NomeTabela));
        }
    }

    private void AvisarQuaseIguais(Unidade unidade, ColunaEsquema coluna)
    {
        var tabela = unidade.Origem!;
        foreach (var outra in tabela.Colunas.Where(outra => !string.Equals(outra.Nome, coluna.Nome, StringComparison.Ordinal) && string.Equals(outra.Nome, coluna.Nome, StringComparison.OrdinalIgnoreCase)))
        {
            _coletor.Aviso(
                new Aviso(CodigosPlanejar.NomeQuaseIgual, $"A coluna {NomeTabela.Parte(outra.Nome)} difere desta só na caixa: confira se a entrada designa a coluna certa.", unidade.NomeTabela, coluna.Nome, outra.Nome),
                Indice(tabela, coluna));
        }
    }

    private static bool QuaseIgual(NomeObjeto nome, NomeObjeto outro) =>
        !nome.Equals(outro)
        && string.Equals(nome.Esquema, outro.Esquema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(nome.Nome, outro.Nome, StringComparison.OrdinalIgnoreCase);

    private string DicaDeTabela(NomeTabelaEscrito nome)
    {
        var parecida = _origem.Esquema.Tabelas.Concat(_destino.Esquema.Tabelas).Select(tabela => tabela.Nome)
            .FirstOrDefault(tabela => string.Equals(tabela.Esquema, nome.Esquema.Nome, StringComparison.OrdinalIgnoreCase) && string.Equals(tabela.Nome, nome.Tabela.Nome, StringComparison.OrdinalIgnoreCase));
        return parecida is null ? "" : $" Há {Exibir(parecida)}, que só difere na caixa: se é ela, escreva o nome entre aspas, como o catálogo o guarda.";
    }

    private static string DicaDeColuna(TabelaEsquema tabela, Identificador nome) =>
        tabela.Colunas.FirstOrDefault(coluna => string.Equals(coluna.Nome, nome.Nome, StringComparison.OrdinalIgnoreCase)) is { } parecida
            ? $" Há {NomeTabela.Parte(parecida.Nome)}, que só difere na caixa: se é ela, escreva o nome entre aspas, como o catálogo o guarda."
            : "";

    // A colação se comporta igual: as marcas do tipo, e da colação o comportamento, nunca o nome; duas não determinísticas
    // com nomes diferentes contam como diferentes.
    private static bool MesmaColacao(ColunaEsquema origem, ColunaEsquema destino)
    {
        if (origem.IgnoraCaixaPeloTipo != destino.IgnoraCaixaPeloTipo || origem.IgnoraEspacoFinalPeloTipo != destino.IgnoraEspacoFinalPeloTipo)
        {
            return false;
        }

        if (origem.Colacao is not { } uma || destino.Colacao is not { } outra)
        {
            return origem.Colacao is null && destino.Colacao is null;
        }

        if (!uma.Deterministica && !outra.Deterministica && (uma.Nome != outra.Nome || uma.Esquema != outra.Esquema))
        {
            return false;
        }

        return uma.Deterministica == outra.Deterministica && uma.TemRegras == outra.TemRegras
            && uma.IgnoraCaixa == outra.IgnoraCaixa && uma.IgnoraAcento == outra.IgnoraAcento && uma.IgnoraPontuacao == outra.IgnoraPontuacao;
    }

    // Mais estreita no destino: comprimento menor (ou com limite onde a origem não tem, ou em outra unidade), faixa menor,
    // menos dígitos inteiros ou de escala.
    private static bool MaisEstreita(ColunaEsquema origem, ColunaEsquema destino)
    {
        if (destino.Comprimento is { Valor: { } limite } comprimento
            && (origem.Comprimento is not { Valor: { } daOrigem } || comprimento.Unidade != origem.Comprimento.Unidade || limite < daOrigem))
        {
            return true;
        }

        if (origem.Faixa is { } faixa && destino.Faixa is { } outra && (outra.Minimo > faixa.Minimo || outra.Maximo < faixa.Maximo))
        {
            return true;
        }

        if (destino.Precisao is { } precisao)
        {
            if (origem.Precisao is not { } precisaoDaOrigem)
            {
                return true;
            }

            var escala = destino.Escala ?? 0;
            var escalaDaOrigem = origem.Escala ?? 0;
            return precisao - escala < precisaoDaOrigem - escalaDaOrigem || escala < escalaDaOrigem;
        }

        return false;
    }

    private static string Tipo(ColunaEsquema coluna) => coluna.Tipo?.ToString() ?? $"sem forma canônica ({coluna.Situacao})";

    private static int Indice(TabelaEsquema tabela, ColunaEsquema coluna) => tabela.Colunas.ToList().IndexOf(coluna);

    private PosicaoConfiguracao NoYaml(Posicao posicao, string local) => new(_configuracao.Arquivo, posicao.Linha, posicao.Coluna, local);

    // O nome como foi escrito, escapado, com as aspas se foram escritas.
    private static string Escrito(Identificador nome) =>
        nome.EntreAspas ? $"\"{TextoSeguro.Nome(nome.Nome).Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : TextoSeguro.Nome(nome.Nome);

    private static string Escrito(NomeTabelaEscrito nome) => $"{Escrito(nome.Esquema)}.{Escrito(nome.Tabela)}";

    private static string Exibir(NomeObjeto nome) => new NomeTabela(nome.Esquema, nome.Nome).ToString();

    private static string Lista(IEnumerable<NomeObjeto> nomes) => string.Join(", ", nomes.Select(Exibir));

    private enum FalhaDeNome
    {
        Sem,
        Nenhuma,
        Ambigua,
    }

    // Uma unidade de cópia: a tabela da origem e a do destino que lhe corresponde, se há.
    private sealed class Unidade(TabelaEsquema? origem)
    {
        internal TabelaEsquema? Origem { get; } = origem;

        internal TabelaEsquema? Destino { get; set; }

        internal bool Ambigua { get; set; }

        internal NomeObjeto Nome => (Origem ?? Destino)!.Nome;

        internal NomeTabela NomeTabela => new(Nome.Esquema, Nome.Nome);
    }
}
