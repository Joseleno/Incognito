using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O hash da configuração: SHA-256, sem chave, do prefixo <c>incognito/hash-configuracao/v1</c> seguido de um byte zero
/// e da serialização canônica das regras resolvidas, publicado como <c>sha256:</c> e 64 dígitos hexadecimais minúsculos.
/// Entram a versão do arquivo, as conexões (provedor, a referência como escrita e o banco esperado), a chave como escrita,
/// o modo, o ambiente, a consistência efetiva e o paralelismo escrito, os domínios da sessão (transformador e versão
/// resolvidos, parâmetros de identidade normalizados, os parâmetros do domínio e políticas efetivas) e cada tabela escrita, com a ação e as regras de coluna. Nunca entram o valor de um segredo, a
/// chave, o esquema lido, os comentários do arquivo nem a justificativa.
/// </summary>
/// <remarks>
/// Cada texto entra como UTF-8 com o tamanho em 4 bytes big-endian na frente; o nulo, como o tamanho <c>FFFFFFFF</c>; cada
/// lista, com a quantidade na frente; um registro opcional presente (a chave, o <c>derivarDe</c>), com a quantidade 1;
/// números em decimal e lógicos como <c>true</c> e <c>false</c>; os enums com a grafia do YAML, fixa no formato. Tabelas, colunas,
/// domínios e parâmetros vão em ordem ordinal (unidades UTF-16), e o nome escrito leva junto se estava entre aspas. O
/// formato é contrato, preso pelo vetor <c>hash-configuracao@1.json</c> e conferido por uma referência independente;
/// mudar qualquer byte exige um prefixo <c>v2</c>.
/// </remarks>
internal static class HashConfiguracao
{
    private const string Prefixo = "incognito/hash-configuracao/v1";
    private const string PrefixoPublicado = "sha256:";

    // A única versão do arquivo que a leitura aceita.
    private const int VersaoDoArquivo = 1;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>O hash da configuração resolvida.</summary>
    /// <param name="configuracao">A configuração lida.</param>
    /// <param name="regras">As regras resolvidas contra o registro.</param>
    /// <returns><c>sha256:</c> e 64 dígitos hexadecimais minúsculos.</returns>
    internal static string Calcular(ConfiguracaoIncognito configuracao, ResolucaoDeRegras regras) =>
        PrefixoPublicado + Convert.ToHexStringLower(SHA256.HashData(Serializar(configuracao, regras)));

    /// <summary>Os bytes que entram no SHA-256, com o prefixo.</summary>
    /// <param name="configuracao">A configuração lida.</param>
    /// <param name="regras">As regras resolvidas contra o registro.</param>
    /// <returns>Os bytes.</returns>
    internal static byte[] Serializar(ConfiguracaoIncognito configuracao, ResolucaoDeRegras regras)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(regras);
        var saida = new Serializacao();
        saida.Bruto([.. Encoding.ASCII.GetBytes(Prefixo), 0]);
        saida.Texto(VersaoDoArquivo.ToString(CultureInfo.InvariantCulture));
        Conexao(saida, configuracao.Origem);
        Conexao(saida, configuracao.Destino);
        saida.Texto(Grafia(configuracao.Modo));
        if (configuracao.Chave is { } chave)
        {
            saida.Quantidade(1);
            Referencia(saida, chave);
        }
        else
        {
            saida.Nulo();
        }

        saida.Texto(configuracao.Ambiente);
        // As políticas de entrada da raiz entram pelas efetivas de cada domínio e de cada coluna; escrever o padrão não muda nada.
        var politicas = configuracao.Politicas;
        saida.Texto(Grafia(politicas.Consistencia ?? Consistencia.MelhorEsforco));
        saida.Opcional(politicas.Paralelismo?.ToString(CultureInfo.InvariantCulture));

        var dominios = regras.Dominios.OrderBy(dominio => dominio.Nome, StringComparer.Ordinal).ToArray();
        saida.Quantidade(dominios.Length);
        foreach (var dominio in dominios)
        {
            saida.Texto(dominio.Nome);
            saida.Texto(dominio.Regra.Dominio.Versao.ToString(CultureInfo.InvariantCulture));
            saida.Texto(dominio.Transformador);
            Parametros(saida, regras.Identidades.GetValueOrDefault(dominio.Nome) ?? new Dictionary<string, string>());
            Parametros(saida, dominio.Regra.Parametros);
            saida.Texto(Grafia(dominio.Regra.EntradaInvalida));
            saida.Texto(Grafia(dominio.Regra.EntradaMalformada));
        }

        var resolvidas = regras.Colunas.ToDictionary<ColunaResolvida, RegraColunaEscrita>(resolvida => resolvida.Coluna, ReferenceEqualityComparer.Instance);
        var tabelas = configuracao.Tabelas
            .OrderBy(tabela => tabela.Nome.Esquema.Nome, StringComparer.Ordinal)
            .ThenBy(tabela => tabela.Nome.Esquema.EntreAspas)
            .ThenBy(tabela => tabela.Nome.Tabela.Nome, StringComparer.Ordinal)
            .ThenBy(tabela => tabela.Nome.Tabela.EntreAspas)
            .ToArray();
        saida.Quantidade(tabelas.Length);
        foreach (var tabela in tabelas)
        {
            Identificador(saida, tabela.Nome.Esquema);
            Identificador(saida, tabela.Nome.Tabela);
            saida.Texto(Grafia(tabela.Acao));
            saida.Logico(tabela.AcaoEscrita);
            var colunas = tabela.Colunas.OrderBy(coluna => coluna.Coluna.Nome, StringComparer.Ordinal).ThenBy(coluna => coluna.Coluna.EntreAspas).ToArray();
            saida.Quantidade(colunas.Length);
            foreach (var coluna in colunas)
            {
                Coluna(saida, coluna, resolvidas.GetValueOrDefault(coluna));
            }
        }

        return saida.Bytes();
    }

    // As grafias do formato v1, fixas aqui: o leitor do YAML pode ganhar sinônimos sem mudar o hash.
    private static string Grafia(ModoTratamento modo) => modo switch
    {
        ModoTratamento.Reproduzivel => "reproduzivel",
        ModoTratamento.Efemero => "efemero",
        _ => throw new ArgumentOutOfRangeException(nameof(modo)),
    };

    private static string Grafia(ProvedorBanco provedor) => provedor switch
    {
        ProvedorBanco.PostgreSql => "postgresql",
        ProvedorBanco.SqlServer => "sqlserver",
        _ => throw new ArgumentOutOfRangeException(nameof(provedor)),
    };

    private static string Grafia(Consistencia consistencia) => consistencia switch
    {
        Consistencia.MelhorEsforco => "melhorEsforco",
        Consistencia.Exigir => "exigir",
        _ => throw new ArgumentOutOfRangeException(nameof(consistencia)),
    };

    private static string Grafia(AcaoTabela acao) => acao switch
    {
        AcaoTabela.Copiar => "copiar",
        AcaoTabela.Esvaziar => "esvaziar",
        AcaoTabela.Ignorar => "ignorar",
        _ => throw new ArgumentOutOfRangeException(nameof(acao)),
    };

    private static string Grafia(PoliticaEntradaInvalida politica) => politica switch
    {
        PoliticaEntradaInvalida.Preservar => "preservar",
        PoliticaEntradaInvalida.Falhar => "falhar",
        PoliticaEntradaInvalida.Anular => "anular",
        _ => throw new ArgumentOutOfRangeException(nameof(politica)),
    };

    private static string Grafia(PoliticaEntradaMalformada politica) => politica switch
    {
        PoliticaEntradaMalformada.Falhar => "falhar",
        PoliticaEntradaMalformada.Anular => "anular",
        PoliticaEntradaMalformada.Preservar => "preservar",
        _ => throw new ArgumentOutOfRangeException(nameof(politica)),
    };

    private static void Conexao(Serializacao saida, ConexaoBanco conexao)
    {
        saida.Texto(Grafia(conexao.Provedor));
        Referencia(saida, conexao.Conexao);
        saida.Opcional(conexao.BancoEsperado);
    }

    // A referência como escrita, nunca o alvo resolvido: o caminho absoluto muda por máquina e leva o nome do usuário.
    private static void Referencia(Serializacao saida, ReferenciaSegredo referencia)
    {
        saida.Texto(referencia.Tipo == TipoReferenciaSegredo.Ambiente ? "ambiente" : "arquivo");
        saida.Texto(referencia.Escrito);
    }

    private static void Coluna(Serializacao saida, RegraColunaEscrita coluna, ColunaResolvida? resolvida)
    {
        Identificador(saida, coluna.Coluna);
        saida.Texto(coluna.Tipo switch
        {
            TipoRegraColuna.Manter => "manter",
            TipoRegraColuna.Dominio => "dominio",
            _ => "transformador",
        });
        saida.Opcional(resolvida is null ? null : resolvida.Regra.Dominio.ToString());
        saida.Opcional(resolvida is null ? null : $"{resolvida.Fabrica.Nome}@{resolvida.Fabrica.Versao.ToString(CultureInfo.InvariantCulture)}");
        if (coluna.DerivarDe is { } derivarDe)
        {
            saida.Quantidade(1);
            Identificador(saida, derivarDe);
        }
        else
        {
            saida.Nulo();
        }

        Parametros(saida, resolvida?.Normalizados ?? new Dictionary<string, string>());
        saida.Opcional(resolvida is null ? null : Grafia(resolvida.Regra.EntradaInvalida));
        saida.Opcional(resolvida is null ? null : Grafia(resolvida.Regra.EntradaMalformada));
        saida.Logico(coluna.Sensivel);
    }

    private static void Identificador(Serializacao saida, Identificador nome)
    {
        saida.Texto(nome.Nome);
        saida.Logico(nome.EntreAspas);
    }

    private static void Parametros(Serializacao saida, IReadOnlyDictionary<string, string> parametros)
    {
        var ordenados = parametros.OrderBy(parametro => parametro.Key, StringComparer.Ordinal).ToArray();
        saida.Quantidade(ordenados.Length);
        foreach (var (nome, valor) in ordenados)
        {
            saida.Texto(nome);
            saida.Texto(valor);
        }
    }

    private sealed class Serializacao
    {
        private readonly ArrayBufferWriter<byte> _bytes = new();

        internal void Bruto(ReadOnlySpan<byte> bytes) => _bytes.Write(bytes);

        internal void Texto(string texto)
        {
            var bytes = _utf8.GetBytes(texto);
            Quantidade(bytes.Length);
            Bruto(bytes);
        }

        internal void Opcional(string? texto)
        {
            if (texto is null)
            {
                Nulo();
            }
            else
            {
                Texto(texto);
            }
        }

        internal void Logico(bool valor) => Texto(valor ? "true" : "false");

        internal void Nulo() => Inteiro(uint.MaxValue);

        internal void Quantidade(int quantidade) => Inteiro(checked((uint)quantidade));

        internal byte[] Bytes() => _bytes.WrittenSpan.ToArray();

        private void Inteiro(uint valor)
        {
            Span<byte> quatro = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(quatro, valor);
            Bruto(quatro);
        }
    }
}
