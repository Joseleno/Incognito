using System.Text;
using CodeProcess.Incognito.Configuracao.Leitura;
using Microsoft.Win32.SafeHandles;

namespace CodeProcess.Incognito.Configuracao;

/// <summary>
/// O <c>incognito.yaml</c> lido e validado: origem, destino, modo, referência à chave, ambiente, políticas, domínios e
/// regras das tabelas. Imutável; guarda só referências a segredos (<c>ambiente:NOME</c>, <c>arquivo:caminho</c>), nunca a
/// chave nem as strings de conexão.
/// </summary>
/// <remarks>
/// <para>
/// A leitura recusa o que pode esconder ou vazar alguma coisa: segredo por extenso em qualquer lugar do arquivo
/// (comentários inclusive), âncoras, aliases, tags e diretivas do YAML, chave desconhecida ou repetida, controles e
/// quebras de linha Unicode. Escalares sem aspas seguem o core schema do YAML 1.2: <c>yes</c> é texto, <c>~</c> é ausente.
/// </para>
/// <para>
/// O arquivo tem a sensibilidade de código: quem o edita escolhe o destino da cópia e o que vai em claro. Não rode um
/// <c>incognito.yaml</c> vindo de fonte não confiável com acesso aos bancos de produção.
/// </para>
/// </remarks>
public sealed class ConfiguracaoIncognito
{
    /// <summary>Tamanho máximo do arquivo, em bytes UTF-8 (16 MiB).</summary>
    internal const int TamanhoMaximo = 16 * 1024 * 1024;

    internal ConfiguracaoIncognito(
        string? arquivo,
        ConexaoBanco origem,
        ConexaoBanco destino,
        ModoTratamento modo,
        ReferenciaSegredo? chave,
        string ambiente,
        PoliticasGerais politicas,
        IReadOnlyList<RegraDominio> dominios,
        IReadOnlyList<RegraTabela> tabelas)
    {
        Arquivo = arquivo;
        Origem = origem;
        Destino = destino;
        Modo = modo;
        Chave = chave;
        Ambiente = ambiente;
        Politicas = politicas;
        Dominios = dominios;
        Tabelas = tabelas;
    }

    internal string? Arquivo { get; }

    internal ConexaoBanco Origem { get; }

    internal ConexaoBanco Destino { get; }

    internal ModoTratamento Modo { get; }

    internal ReferenciaSegredo? Chave { get; }

    internal string Ambiente { get; }

    internal PoliticasGerais Politicas { get; }

    internal IReadOnlyList<RegraDominio> Dominios { get; }

    internal IReadOnlyList<RegraTabela> Tabelas { get; }

    /// <summary>
    /// Lê e valida o arquivo. Um <c>arquivo:</c> relativo vale a partir da pasta dele. O arquivo precisa estar em UTF-8
    /// (com ou sem BOM), com até 16 MiB.
    /// </summary>
    /// <param name="caminho">Caminho do <c>incognito.yaml</c>.</param>
    /// <exception cref="ArgumentException">O caminho é nulo, vazio ou só espaços.</exception>
    /// <exception cref="ConfiguracaoInvalidaException">
    /// O arquivo não pôde ser lido ou não passou na validação; <see cref="ConfiguracaoInvalidaException.Erros"/> traz
    /// cada erro com linha e coluna.
    /// </exception>
    public static ConfiguracaoIncognito CarregarYaml(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        var erros = new ColetorDeErros(caminho);
        string pasta;
        byte[] bytes;
        try
        {
            pasta = Path.GetDirectoryName(Path.GetFullPath(caminho)) ?? Path.GetFullPath(".");
            bytes = LerArquivo(caminho);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Nunca a mensagem do sistema: ela pode trazer outros caminhos.
            erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "Não foi possível ler o arquivo (não existe, é uma pasta ou não há permissão).");
            throw erros.Excecao();
        }

        if (bytes.Length > TamanhoMaximo)
        {
            erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O arquivo passa de 16 MiB.");
            throw erros.Excecao();
        }

        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O arquivo está em UTF-16; grave-o em UTF-8.");
            throw erros.Excecao();
        }

        string texto;
        try
        {
            texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O arquivo não é UTF-8 válido; grave-o em UTF-8.");
            throw erros.Excecao();
        }

        return Ler(texto, caminho, pasta, erros);
    }

    /// <summary>
    /// Lê e valida o texto de um <c>incognito.yaml</c>. Aqui um <c>arquivo:</c> precisa de caminho absoluto, porque não há
    /// pasta de referência.
    /// </summary>
    /// <param name="texto">O conteúdo do arquivo, com até 16 MiB em UTF-8.</param>
    /// <exception cref="ArgumentNullException">O texto é nulo.</exception>
    /// <exception cref="ConfiguracaoInvalidaException">
    /// O texto não passou na validação; <see cref="ConfiguracaoInvalidaException.Erros"/> traz cada erro com linha e
    /// coluna.
    /// </exception>
    public static ConfiguracaoIncognito LerYaml(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var erros = new ColetorDeErros(null);
        if (texto.Length > TamanhoMaximo || Encoding.UTF8.GetByteCount(texto) > TamanhoMaximo)
        {
            erros.Adicionar(CodigosConfiguracao.Arquivo, null, "", "O texto passa de 16 MiB.");
            throw erros.Excecao();
        }

        return Ler(texto, null, null, erros);
    }

    private static ConfiguracaoIncognito Ler(string texto, string? arquivo, string? pasta, ColetorDeErros erros)
    {
        texto = PassadaDeCaracteres.Conferir(texto, erros);
        if (!erros.Vazio)
        {
            // O aviso de segredo por extenso sai mesmo quando o arquivo é recusado por outro motivo.
            VarreduraDeSegredos.Varrer(texto, null, erros);
            throw erros.Excecao();
        }

        var raiz = LeitorYaml.Ler(texto, erros);
        if (raiz is not null)
        {
            PassadaDeCaracteres.ConferirEscalares(raiz, erros);
        }

        VarreduraDeSegredos.Varrer(texto, raiz, erros);
        if (raiz is null || !erros.Vazio)
        {
            throw erros.Excecao();
        }

        return ValidadorConfiguracao.Validar(raiz, erros, pasta, arquivo) ?? throw erros.Excecao();
    }

    // Lê até um byte além do limite, sem confiar no tamanho informado (um dispositivo informa 0 e não acaba).
    private static byte[] LerArquivo(string caminho)
    {
        using SafeFileHandle arquivo = File.OpenHandle(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[64 * 1024];
        using var lidos = new MemoryStream();
        long posicao = 0;
        while (lidos.Length <= TamanhoMaximo)
        {
            var quantidade = RandomAccess.Read(arquivo, buffer, posicao);
            if (quantidade == 0)
            {
                break;
            }

            lidos.Write(buffer, 0, quantidade);
            posicao += quantidade;
        }

        return lidos.ToArray();
    }
}
