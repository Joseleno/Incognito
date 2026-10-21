using System.Buffers;
using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Interno;

namespace CodeProcess.Incognito.Execucao;

/// <summary>
/// O plano de uma cópia: o que o <c>planejar</c> decidiu fazer com cada tabela e cada coluna da origem, com as regras
/// efetivas e os avisos. Nunca traz a chave, a string de conexão nem valor de linha.
/// </summary>
public sealed class Plano
{
    private const string PrefixoDoHash = "sha256:";

    private static readonly SearchValues<char> _hexMinusculo = SearchValues.Create("0123456789abcdef");

    internal Plano(IEnumerable<MapaTabela> tabelas, IEnumerable<Aviso> avisos, string hashConfiguracao, DadosDeExecucao execucao)
    {
        ArgumentNullException.ThrowIfNull(tabelas);
        ArgumentNullException.ThrowIfNull(avisos);
        ArgumentNullException.ThrowIfNull(hashConfiguracao);
        ArgumentNullException.ThrowIfNull(execucao);
        if (!HashValido(hashConfiguracao))
        {
            throw new ArgumentException("O hash da configuração é \"sha256:\" seguido de 64 dígitos hexadecimais minúsculos.", nameof(hashConfiguracao));
        }

        Modo = execucao.Configuracao.Modo;
        Tabelas = [.. tabelas];
        Avisos = [.. avisos];
        HashConfiguracao = hashConfiguracao;
        Execucao = execucao;
        foreach (var tabela in Tabelas)
        {
            ArgumentNullException.ThrowIfNull(tabela, nameof(tabelas));
        }

        foreach (var aviso in Avisos)
        {
            ArgumentNullException.ThrowIfNull(aviso, nameof(avisos));
        }

        // O que se publica e o que se executa descrevem as mesmas unidades, na mesma ordem e com a mesma ação, e as mesmas
        // colunas transformadas: uma regra publicada sem transformador na carga sairia em claro.
        if (Tabelas.Count != execucao.Unidades.Count
            || Tabelas.Zip(execucao.Unidades).Any(par => !par.First.Nome.Equals(par.Second.Nome) || par.First.Acao != par.Second.Acao
                || !par.First.Colunas.Where(coluna => coluna.Regra?.Tipo == TipoRegra.Transformar).Select(coluna => coluna.Nome)
                    .SequenceEqual(par.Second.Tratadas.Select(tratada => tratada.Coluna), StringComparer.Ordinal)))
        {
            throw new ArgumentException("As unidades da carga não correspondem às tabelas do plano.", nameof(execucao));
        }
    }

    /// <summary>O modo de tratamento.</summary>
    public ModoTratamento Modo { get; }

    /// <summary>
    /// Todas as unidades de cópia dos dois lados, em ordem ordinal de esquema e tabela: as da origem e as que só existem no
    /// destino (com <c>esvaziar</c> ou <c>ignorar</c> escrito).
    /// </summary>
    public IReadOnlyList<MapaTabela> Tabelas { get; }

    /// <summary>Os avisos, em ordem de tabela, posição da coluna, código e objeto.</summary>
    public IReadOnlyList<Aviso> Avisos { get; }

    /// <summary>
    /// O hash da configuração que gerou o plano, como <c>sha256:</c> seguido de 64 dígitos hexadecimais minúsculos. Sai
    /// das regras resolvidas, não dos bytes do arquivo: comentários, quebras de linha e justificativas não o mudam. Nunca
    /// entram o valor dos segredos nem a chave; as referências entram como escritas.
    /// </summary>
    public string HashConfiguracao { get; }

    /// <summary>O que a execução lê e o plano não publica.</summary>
    internal DadosDeExecucao Execucao { get; }

    /// <summary>No formato <c>Plano sha256:… (n tabelas, m avisos)</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Plano {HashConfiguracao} ({Contagem(Tabelas.Count, "tabela", "tabelas")}, {Contagem(Avisos.Count, "aviso", "avisos")})");

    private static string Contagem(int quantidade, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{quantidade} {(quantidade == 1 ? singular : plural)}");

    private static bool HashValido(string hash) =>
        hash.Length == PrefixoDoHash.Length + 64
        && hash.StartsWith(PrefixoDoHash, StringComparison.Ordinal)
        && !hash.AsSpan(PrefixoDoHash.Length).ContainsAnyExcept(_hexMinusculo);
}
