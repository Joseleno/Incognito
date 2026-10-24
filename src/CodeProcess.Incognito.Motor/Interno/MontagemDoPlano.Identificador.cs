// O tamanho do domínio do id.permutar lê as chaves e as permissões do esquema que o dialeto devolveu, tipos do contrato
// experimental de dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O tamanho de cada domínio do <c>id.permutar</c> (P6b, depois da propagação): as fontes são as colunas do domínio na
/// chave primária da origem, em qualquer posição, numa unidade copiada, e as herdeiras ligadas a elas pela herança; o N é a
/// menor potência de 10, a partir de 10⁴, acima de dez vezes o maior valor delas, como a cópia lê. As outras colunas do
/// domínio sem chave estrangeira imposta e validada até uma fonte também são lidas: um valor a partir de N falharia na
/// carga, e sai no plano. O maior valor lido nunca vira texto: fica nesta montagem até o N ser calculado.
/// </summary>
internal sealed partial class MontagemDoPlano
{
    private const string IdPermutar = "id.permutar";

    // O maior valor a partir do qual N passaria de 10¹⁸.
    private const long MaiorComportado = 100_000_000_000_000_000;

    private readonly Dictionary<ReferenciaColuna, (No No, bool Fonte)> _leituras = [];
    private readonly Dictionary<DominioPseudonimo, long> _maiores = [];
    private readonly Dictionary<No, long> _dasOutras = [];
    private readonly HashSet<No> _fontes = [];
    private readonly HashSet<DominioPseudonimo> _semTamanho = [];
    private readonly Dictionary<DominioPseudonimo, long> _tamanhos = [];

    /// <summary>As colunas da origem cujo maior valor o planejar lê, em ordem de tabela e coluna.</summary>
    internal IReadOnlyList<ReferenciaColuna> Leituras { get; private set; } = [];

    /// <summary>A conta do N: a menor potência de 10, a partir de 10⁴, acima de dez vezes o maior valor (zero e negativos contam zero).</summary>
    /// <param name="maior">O maior valor das fontes.</param>
    /// <returns>O N; nulo quando passaria de 10¹⁸.</returns>
    internal static long? Tamanho(long maior)
    {
        if (maior >= MaiorComportado)
        {
            return null;
        }

        // 10 elevado ao número de dígitos do maior, sem multiplicar o maior: com ele abaixo de 10¹⁷, cabe com folga.
        var potencia = 10L;
        while (potencia <= maior)
        {
            potencia *= 10;
        }

        return Math.Max(10_000, potencia * 10);
    }

    /// <summary>Anota o maior valor lido de uma coluna, que só fica nesta montagem.</summary>
    /// <param name="coluna">Uma das <see cref="Leituras"/>.</param>
    /// <param name="maior">O maior valor lido; nulo sem linha ou só com nulos.</param>
    /// <exception cref="InvalidOperationException">A coluna não é uma das leituras (defeito do motor).</exception>
    internal void Anotar(ReferenciaColuna coluna, long? maior)
    {
        ArgumentNullException.ThrowIfNull(coluna);
        if (!_leituras.TryGetValue(coluna, out var leitura))
        {
            throw new InvalidOperationException("O planejar anotou o maior valor de uma coluna que não pediu.");
        }

        var dominio = _efetivas[leitura.No].Resolvida.Regra.Dominio;
        if (maior is not > 0)
        {
            return;
        }

        if (leitura.Fonte)
        {
            _maiores[dominio] = Math.Max(_maiores.GetValueOrDefault(dominio), maior.Value);
        }
        else
        {
            _dasOutras[leitura.No] = maior.Value;
        }
    }

    // P6b: as fontes e as outras colunas a ler de cada domínio. Um domínio com coluna que não se lê (sem permissão, com a
    // segurança por linha, sem forma inteira) fica sem N, sem ler nada: o erro da coluna sai no P7 ou no P9.
    private void PrepararTamanhos()
    {
        var porDominio = _efetivas
            .Where(par => Permutar(par.Value.Resolvida.Fabrica))
            .GroupBy(par => par.Value.Resolvida.Regra.Dominio, par => par.Key)
            .OrderBy(grupo => grupo.Key.Nome, StringComparer.Ordinal);
        var leituras = new List<(No No, bool Fonte)>();
        foreach (var grupo in porDominio)
        {
            // O componente recusado sai: os erros dele já dizem o que corrigir. Se a fonte estava nele, o domínio fica sem
            // N, sem um INC3091 a mais.
            var colunas = grupo.Where(no => !_recusadas.Contains(no)).ToHashSet();
            if (colunas.Count == 0)
            {
                continue;
            }

            var fontes = Fontes(colunas);
            if (fontes.Count == 0 && Fontes(grupo.ToHashSet()).Count > 0)
            {
                _semTamanho.Add(grupo.Key);
                continue;
            }

            if (fontes.Count == 0)
            {
                ErroDoDominio(
                    CodigosPlanejar.DominioSemFonte,
                    grupo.Key,
                    $"O domínio {grupo.Key.Nome} do id.permutar não tem coluna na chave primária de uma tabela copiada da origem: sem ela, o tamanho do domínio (N) não se calcula. Declare o domínio na coluna da chave primária, ou copie a tabela dela.");
                continue;
            }

            _fontes.UnionWith(fontes);
            var cobertas = Cobertas(colunas, fontes);
            var aLer = colunas.Where(no => !cobertas.Contains(no)).Order(Comparer<No>.Create(Comparar)).ToList();
            if (aLer.Any(no => !Legivel(no)))
            {
                _semTamanho.Add(grupo.Key);
                continue;
            }

            leituras.AddRange(aLer.Select(no => (no, fontes.Contains(no))));
        }

        foreach (var (no, fonte) in leituras)
        {
            _leituras[new ReferenciaColuna(no.Unidade.Origem!.Nome, no.Coluna)] = (no, fonte);
        }

        Leituras = [.. _leituras.Keys];
    }

    // Os N, depois das leituras: o maior das fontes dá o N, e as outras colunas com valor a partir dele recusam o plano
    // quando a entrada malformada falha. O maior valor sai da montagem aqui.
    private void CalcularTamanhos()
    {
        foreach (var dominio in _leituras.Values.Select(leitura => _efetivas[leitura.No].Resolvida.Regra.Dominio).Distinct())
        {
            if (Tamanho(_maiores.GetValueOrDefault(dominio)) is { } tamanho)
            {
                _tamanhos[dominio] = tamanho;
                continue;
            }

            _semTamanho.Add(dominio);
            ErroDoDominio(
                CodigosPlanejar.TamanhoNaoCabe,
                dominio,
                $"O maior valor da chave primária do domínio {dominio.Nome} passa do que o id.permutar comporta: o tamanho do domínio (N) passaria de 10^18. Use outro transformador nas colunas do domínio.");
        }

        foreach (var (no, maior) in _dasOutras.OrderBy(par => par.Key, Comparer<No>.Create(Comparar)))
        {
            var efetiva = _efetivas[no];
            if (_tamanhos.TryGetValue(efetiva.Resolvida.Regra.Dominio, out var tamanho) && maior >= tamanho && efetiva.Resolvida.Regra.EntradaMalformada == PoliticaEntradaMalformada.Falhar)
            {
                _coletor.Erro(
                    new ErroPlano(
                        CodigosPlanejar.ForaDoDominio,
                        $"A coluna tem valor a partir de N = {Potencia(tamanho)}, o tamanho do domínio {efetiva.Resolvida.Regra.Dominio.Nome} calculado pela chave primária, e não chega a ela por chave estrangeira imposta e validada (linha órfã, chave não validada ou só a citação do domínio): com 'entradaMalformada: falhar', a cópia falharia nessa linha. Corrija as linhas e valide a chave estrangeira na origem.{Propagada(efetiva)}",
                        no.Unidade.NomeTabela,
                        no.Coluna,
                        posicao: NoYaml(efetiva.Escrita.Posicao, efetiva.Escrita.Local)),
                    no.Unidade.Origem!.Colunas.ToList().FindIndex(coluna => coluna.Nome == no.Coluna));
            }
        }

        _maiores.Clear();
        _dasOutras.Clear();
    }

    // O N de uma coluna tratada no P7: nulo fora do id.permutar; sem N, o erro do domínio já saiu (ou sai no P9).
    private (bool Permutada, long? Tamanho) TamanhoDa(ColunaResolvida resolvida) =>
        !Permutar(resolvida.Fabrica) ? (false, null)
        : _tamanhos.TryGetValue(resolvida.Regra.Dominio, out var tamanho) ? (true, tamanho)
        : (true, null);

    // As fontes: as colunas do domínio na chave primária da tabela da origem, em unidade copiada e local, e as herdeiras
    // ligadas pela herança a uma fonte (lidas à parte, porque a cópia lê cada unidade sem as herdeiras).
    private HashSet<No> Fontes(HashSet<No> colunas)
    {
        var fontes = colunas.Where(no => Local(no.Unidade) && no.Unidade.Origem!.ChavePrimaria.Contains(no.Coluna, StringComparer.Ordinal)).ToHashSet();
        bool mudou;
        do
        {
            mudou = false;
            foreach (var no in colunas.Where(no => !fontes.Contains(no) && Local(no.Unidade) && no.Unidade.Origem!.Tipo == TipoTabela.Herdeira))
            {
                if (MaesCopiadas(no.Unidade.Origem!).Any(mae => fontes.Contains(new No(mae, no.Coluna))))
                {
                    mudou |= fontes.Add(no);
                }
            }
        }
        while (mudou);

        return fontes;
    }

    // As colunas que chegam a uma fonte por chaves estrangeiras da origem impostas e validadas, de uma coluna só: na chave
    // composta, uma posição nula dispensa a conferência das outras, e o valor pode ser órfão.
    private HashSet<No> Cobertas(HashSet<No> colunas, HashSet<No> fontes)
    {
        var porNome = _origem.Esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        var alcancadas = new HashSet<No>(fontes);
        bool mudou;
        do
        {
            mudou = false;
            foreach (var no in colunas.Where(no => !alcancadas.Contains(no)))
            {
                var chega = no.Unidade.Origem!.ChavesEstrangeiras.Any(chave =>
                    chave is { Imposta: true, Validada: true, Colunas: [var coluna], ColunasReferenciadas: [var referenciada] }
                    && coluna == no.Coluna
                    && porNome.TryGetValue(chave.Referenciada, out var tabela)
                    && _unidadeDe.TryGetValue(tabela, out var pai)
                    && alcancadas.Contains(new No(pai, referenciada)));
                if (chega)
                {
                    mudou |= alcancadas.Add(no);
                }
            }
        }
        while (mudou);

        alcancadas.ExceptWith(fontes);
        return alcancadas;
    }

    // A unidade copiada, sem ambiguidade e local na origem: a estrangeira não é lida (e já recusa o plano).
    private bool Local(Unidade unidade) => !unidade.Ambigua && _pares.ContainsKey(unidade) && unidade.Origem is { Estrangeira: false };

    // Se a coluna se lê como a cópia a leria: inteira na origem, com a leitura da tabela e sem a segurança por linha. O
    // mesmo predicado do P9, que dá o erro da que não se lê.
    private bool Legivel(No no)
    {
        var tabela = no.Unidade.Origem!;
        var coluna = tabela.Colunas.FirstOrDefault(lida => lida.Nome == no.Coluna);
        var permissao = _origem.Esquema.Acesso?.Tabelas.FirstOrDefault(lida => lida.Tabela.Equals(tabela.Nome));
        return Local(no.Unidade) && coluna is { Tipo: TipoColuna.Inteiro, Situacao: SituacaoTipo.Suportado } && permissao is { LeituraFiltradaPorLinha: false, PodeLer: true };
    }

    // Sem N, nenhuma coluna do domínio é criada: o erro que explica precisa ter saído.
    private void ConferirTamanhos()
    {
        if (_semTamanho.Count > 0 && !_coletor.TemErros)
        {
            throw new InvalidOperationException("Domínio do id.permutar sem o tamanho e sem o erro que o explica.");
        }
    }

    private void ErroDoDominio(string codigo, DominioPseudonimo dominio, string mensagem)
    {
        var declaracao = Declaracao(dominio.Nome);
        _coletor.Erro(new ErroPlano(codigo, mensagem + (declaracao?.Coluna is { } coluna ? $" O domínio é declarado em {coluna}." : ""), posicao: declaracao?.Posicao));
    }

    // Onde o domínio foi declarado: na raiz 'dominios', ou numa coluna (o do id.permutar), com as políticas escritas lá.
    private (PosicaoConfiguracao Posicao, PoliticasDeEntrada Politicas, string? Coluna)? Declaracao(string dominio)
    {
        if (_configuracao.Dominios.FirstOrDefault(declarado => declarado.Nome == dominio) is { } naRaiz)
        {
            return (NoYaml(naRaiz.Posicao, naRaiz.Local), naRaiz.Politicas, null);
        }

        foreach (var tabela in _configuracao.Tabelas)
        {
            if (tabela.Colunas.FirstOrDefault(coluna => coluna is { Tipo: TipoRegraColuna.Transformador } && coluna.Dominio == dominio) is { } naColuna)
            {
                return (NoYaml(naColuna.Posicao, naColuna.Local), naColuna.Politicas, $"{Escrito(tabela.Nome)}.{Escrito(naColuna.Coluna)}");
            }
        }

        return null;
    }

    // A regra declara o domínio na própria coluna (o id.permutar com 'dominio').
    private static bool DeclaraNaColuna(RegraColunaEscrita regra) => regra is { Tipo: TipoRegraColuna.Transformador, Dominio: not null };

    // O id.permutar embutido, reconhecido pela referência da fábrica: o prefixo id. é reservado.
    private static bool Permutar(FabricaRegistrada fabrica) => fabrica.Embutida && fabrica.Nome == IdPermutar;

    // N como 10^k, a forma da mensagem.
    private static string Potencia(long tamanho) => $"10^{(tamanho.ToString(CultureInfo.InvariantCulture).Length - 1).ToString(CultureInfo.InvariantCulture)}";
}
