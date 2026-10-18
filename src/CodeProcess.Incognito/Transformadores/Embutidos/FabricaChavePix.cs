using System.Collections.Frozen;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>br.chave-pix@1</c>: chave Pix do mesmo tipo, no domínio padrão <c>chave-pix</c> (o da chave aleatória). CPF, CNPJ,
/// e-mail e telefone vão às instâncias dos domínios citados em <c>dominioCpf</c>, <c>dominioCnpj</c>,
/// <c>dominioEmail</c> e <c>dominioTelefone</c> (padrão: os domínios padrão), as mesmas das colunas desses documentos,
/// com a política de DV do domínio de cada um e a política de malformado da chave. A política de DV da própria regra não
/// se aplica.
/// </summary>
internal sealed class FabricaChavePix() : FabricaEmbutida("br.chave-pix", "chave-pix", Injetividade.Probabilistica, TipoColuna.Texto)
{
    private const string DominioCpf = "dominioCpf";
    private const string DominioCnpj = "dominioCnpj";
    private const string DominioEmail = "dominioEmail";
    private const string DominioTelefone = "dominioTelefone";

    private static readonly FabricaCpf _cpf = new();
    private static readonly FabricaCnpj _cnpj = new();
    private static readonly FabricaEmail _email = new();
    private static readonly FabricaTelefone _telefone = new();

    private static readonly FrozenDictionary<string, ParametroEmbutido> _parametros = new Dictionary<string, ParametroEmbutido>(StringComparer.Ordinal)
    {
        [DominioCpf] = ParametroEmbutido.Dominio(_cpf.DominioPadrao),
        [DominioCnpj] = ParametroEmbutido.Dominio(_cnpj.DominioPadrao),
        [DominioEmail] = ParametroEmbutido.Dominio(_email.DominioPadrao),
        [DominioTelefone] = ParametroEmbutido.Dominio(_telefone.DominioPadrao),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> _citados = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DominioCpf] = _cpf.Nome,
        [DominioCnpj] = _cnpj.Nome,
        [DominioEmail] = _email.Nome,
        [DominioTelefone] = _telefone.Nome,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    protected override FrozenDictionary<string, ParametroEmbutido> ParametrosAceitos => _parametros;

    private protected override IReadOnlyDictionary<string, string> ParametrosDeDominioEmbutido => _citados;

    protected override void ValidarRegra(RegraTransformacao regra)
    {
        // Só na coluna: a declaração de um domínio (sem justificativa possível) vale para várias colunas, e cada uma confere.
        if (regra.EntradaMalformada == PoliticaEntradaMalformada.Preservar && regra.TemJustificativa == false)
        {
            throw new RegraInvalidaException(
                $"Com 'entradaMalformada: preservar', o {Nome}@{Versao} copia em claro o que não tem forma de chave Pix: escreva na coluna a 'justificativa'.");
        }
    }

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        ExigirComprimento(contexto, 36, "o pseudônimo de chave Pix tem 36 caracteres na chave aleatória");
        var sessao = contexto.Sessao;
        var regra = contexto.Regra;
        var normalizados = Normalizar(regra);

        // Os quatro domínios conferidos antes de criar qualquer instância: um ausente não deixa a sessão pela metade.
        var deCpf = Componente(DominioCitado(sessao, normalizados, DominioCpf, _cpf.Nome), regra);
        var deCnpj = Componente(DominioCitado(sessao, normalizados, DominioCnpj, _cnpj.Nome), regra);
        var deEmail = Componente(DominioCitado(sessao, normalizados, DominioEmail, _email.Nome), regra);
        var deTelefone = Componente(DominioCitado(sessao, normalizados, DominioTelefone, _telefone.Nome), regra);

        var cpf = _cpf.Instancia(sessao, deCpf);
        var cnpj = _cnpj.Instancia(sessao, deCnpj);
        var email = _email.Instancia(sessao, deEmail);
        var telefone = _telefone.Instancia(sessao, deTelefone);
        var chavePix = sessao.Obter(Identidade(regra, normalizados), (chave, ambiente) => new PseudonimizadorChavePix(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, cpf, cnpj, email, telefone, regra.EntradaMalformada));
        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, chavePix.Pseudonimizar, null);
    }

    // O documento dentro da chave: o domínio, os parâmetros e a política de DV do domínio dele (decisão 49), e a política
    // de malformado da chave (decisão 50), que o pseudonimizador da chave exige igual nos quatro.
    private static RegraTransformacao Componente(RegraTransformacao doDominio, RegraTransformacao daChave) =>
        new(doDominio.Dominio)
        {
            Parametros = doDominio.Parametros,
            EntradaInvalida = doDominio.EntradaInvalida,
            EntradaMalformada = daChave.EntradaMalformada,
        };
}
