using System.Text.Json;
using CodeProcess.Incognito.Pseudonimizacao;
using CodeProcess.Incognito.Transformadores;
using CodeProcess.Incognito.Transformadores.Embutidos;

namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>
/// Monta os pseudonimizadores de um caso de vetor fixo com todos os parâmetros dele e executa os pares: usado pelo
/// teste dos vetores e pelo gerador. A conferência independente é a da referência em Python.
/// </summary>
internal sealed class ExecutorDeCaso : IDisposable
{
    private readonly List<IDisposable> _descartaveis;
    private readonly Func<EntradaDoPar, ResultadoDoPar> _executar;

    private ExecutorDeCaso(List<IDisposable> descartaveis, Func<EntradaDoPar, ResultadoDoPar> executar)
    {
        _descartaveis = descartaveis;
        _executar = executar;
    }

    /// <summary>Monta o caso. A chave continua de quem chama.</summary>
    internal static ExecutorDeCaso Criar(string transformador, int versao, ChaveIncognito chave, JsonElement caso)
    {
        var descartaveis = new List<IDisposable>();
        try
        {
            return new ExecutorDeCaso(descartaveis, Montar(descartaveis, transformador, versao, chave, caso));
        }
        catch
        {
            Descartar(descartaveis);
            throw;
        }
    }

    /// <summary>Executa um par; o erro da política de falhar vira o código no resultado.</summary>
    internal ResultadoDoPar Executar(EntradaDoPar entrada)
    {
        try
        {
            return _executar(entrada);
        }
        catch (IncognitoException excecao)
        {
            return new ResultadoDoPar(null, null, excecao.Codigo);
        }
    }

    public void Dispose() => Descartar(_descartaveis);

    // Os compostos antes das instâncias que eles usam.
    private static void Descartar(List<IDisposable> descartaveis)
    {
        for (var i = descartaveis.Count - 1; i >= 0; i--)
        {
            descartaveis[i].Dispose();
        }
    }

    private static Func<EntradaDoPar, ResultadoDoPar> Montar(
        List<IDisposable> descartaveis, string transformador, int versao, ChaveIncognito chave, JsonElement caso)
    {
        T Guardar<T>(T descartavel)
            where T : IDisposable
        {
            descartaveis.Add(descartavel);
            return descartavel;
        }

        var dominio = caso.GetProperty("dominio").GetString()!;
        var ambiente = caso.GetProperty("ambiente").GetString()!;
        // O texto.substituir não tem política de entrada malformada.
        var malformada = caso.TryGetProperty("entradaMalformada", out _) ? Malformada(caso) : PoliticaEntradaMalformada.Falhar;
        // Pela versão também: um arquivo de versão sem implementação é recusado, nunca executado com o algoritmo de outra.
        switch ($"{transformador}@{versao}")
        {
            case "br.cpf@1":
                var cpf = Guardar(new PseudonimizadorCpf(chave, dominio, versao, ambiente, Invalida(caso), malformada));
                return entrada => entrada.EhNumero
                    ? Numero(cpf.Pseudonimizar(entrada.Numero, out var numero), numero)
                    : Texto(cpf.Pseudonimizar(entrada.Texto!, out var texto), texto);
            case "br.cnpj@1":
                var cnpj = Guardar(new PseudonimizadorCnpj(
                    chave, dominio, versao, ambiente, caso.GetProperty("permutarOrdem").GetBoolean(), Invalida(caso), malformada));
                return entrada => entrada.EhNumero
                    ? Numero(cnpj.Pseudonimizar(entrada.Numero, out var numero), numero)
                    : Texto(cnpj.Pseudonimizar(entrada.Texto!, out var texto), texto);
            case "br.telefone@1":
                var telefone = Guardar(new PseudonimizadorTelefone(chave, dominio, versao, ambiente, malformada));
                return entrada => entrada.EhNumero
                    ? Numero(telefone.Pseudonimizar(entrada.Numero, out var numero), numero)
                    : Texto(telefone.Pseudonimizar(entrada.Texto!, out var texto), texto);
            case "email@1":
                var email = Guardar(new PseudonimizadorEmail(chave, dominio, versao, ambiente, malformada));
                return entrada => Texto(email.Pseudonimizar(SoTexto(entrada), out var texto), texto);
            case "br.cep@1":
                var cep = Guardar(new PseudonimizadorCep(chave, dominio, versao, ambiente, malformada));
                return entrada => entrada.EhNumero
                    ? Numero(cep.Pseudonimizar(entrada.Numero, entrada.Entidade!, out var numero), numero)
                    : Texto(cep.Pseudonimizar(entrada.Texto!, entrada.Entidade!, out var texto), texto);
            case "id.permutar@1":
                var identificador = Guardar(new PseudonimizadorIdentificador(
                    chave, dominio, versao, ambiente, caso.GetProperty("n").GetInt64(), malformada));
                return entrada => entrada.EhNumero
                    ? Numero(identificador.Pseudonimizar(entrada.Numero, out var numero), numero)
                    : throw new InvalidDataException("O id.permutar só recebe inteiros.");
            case "br.chave-pix@1":
                var componentes = caso.GetProperty("componentes");
                var deCpf = componentes.GetProperty("cpf");
                var deCnpj = componentes.GetProperty("cnpj");
                var deEmail = componentes.GetProperty("email");
                var deTelefone = componentes.GetProperty("telefone");
                var cpfDaChave = Guardar(new PseudonimizadorCpf(
                    chave, Dominio(deCpf), Versao(deCpf), ambiente, Invalida(deCpf), malformada));
                var cnpjDaChave = Guardar(new PseudonimizadorCnpj(
                    chave, Dominio(deCnpj), Versao(deCnpj), ambiente, deCnpj.GetProperty("permutarOrdem").GetBoolean(), Invalida(deCnpj), malformada));
                var emailDaChave = Guardar(new PseudonimizadorEmail(chave, Dominio(deEmail), Versao(deEmail), ambiente, malformada));
                var telefoneDaChave = Guardar(new PseudonimizadorTelefone(chave, Dominio(deTelefone), Versao(deTelefone), ambiente, malformada));
                var chavePix = Guardar(new PseudonimizadorChavePix(
                    chave, dominio, versao, ambiente, cpfDaChave, cnpjDaChave, emailDaChave, telefoneDaChave, malformada));
                return entrada => Texto(chavePix.Pseudonimizar(SoTexto(entrada), out var texto), texto);
            case "br.chave-dfe@1":
                var doEmitente = caso.GetProperty("componentes");
                var deCpfDoEmitente = doEmitente.GetProperty("cpf");
                var deCnpjDoEmitente = doEmitente.GetProperty("cnpj");

                var cpfDoEmitente = Guardar(new PseudonimizadorCpf(
                    chave, Dominio(deCpfDoEmitente), Versao(deCpfDoEmitente), ambiente, ColunasDoEmitente.InvalidaIgnorada, ColunasDoEmitente.MalformadaIgnorada));
                var cnpjDoEmitente = Guardar(new PseudonimizadorCnpj(
                    chave, Dominio(deCnpjDoEmitente), Versao(deCnpjDoEmitente), ambiente, deCnpjDoEmitente.GetProperty("permutarOrdem").GetBoolean(),
                    ColunasDoEmitente.InvalidaIgnorada, ColunasDoEmitente.MalformadaIgnorada));
                var modelos = caso.GetProperty("modelos").EnumerateArray().Select(m => m.GetInt32()).ToArray();
                var chaveDfe = Guardar(new PseudonimizadorChaveDfe(
                    chave, versao, ambiente, cpfDoEmitente, cnpjDoEmitente, modelos, Invalida(caso), malformada));
                return entrada =>
                {
                    var desfecho = chaveDfe.Pseudonimizar(SoTexto(entrada), out var texto, out var ocorrencias);
                    return Texto(desfecho, texto) with { Ocorrencias = Nomes(ocorrencias) };
                };
            case "texto.substituir@1":
                var comprimento = caso.GetProperty("comprimentoMaximo");
                var substituir = new TransformadorTextoSubstituir(comprimento.ValueKind == JsonValueKind.Null ? null : comprimento.GetInt32());
                return entrada =>
                {
                    var resultado = substituir.Transformar(SoTexto(entrada), default);
                    return new ResultadoDoPar(resultado.Valor, resultado.Desfecho.ToString(), null);
                };
            default:
                throw new InvalidDataException("Transformador ou versão sem executor.");
        }
    }

    // Enum.GetValues vem em ordem crescente de valor: a ordem canônica do vetor. Os nomes são contrato dos vetores.
    private static string[] Nomes(OcorrenciasChaveDfe ocorrencias) =>
        [.. Enum.GetValues<OcorrenciasChaveDfe>().Where(o => o != OcorrenciasChaveDfe.Nenhuma && ocorrencias.HasFlag(o)).Select(o => o.ToString())];

    private static string Dominio(JsonElement componente) => componente.GetProperty("dominio").GetString()!;

    private static int Versao(JsonElement componente) => componente.GetProperty("versao").GetInt32();

    private static string SoTexto(EntradaDoPar entrada) =>
        entrada.EhNumero ? throw new InvalidDataException("Este transformador só recebe texto.") : entrada.Texto!;

    private static ResultadoDoPar Texto(Desfecho desfecho, string? saida) => new(saida, desfecho.ToString(), null);

    private static ResultadoDoPar Numero(Desfecho desfecho, long? saida) => new(saida, desfecho.ToString(), null);

    private static PoliticaEntradaInvalida Invalida(JsonElement objeto) => objeto.GetProperty("entradaInvalida").GetString() switch
    {
        "preservar" => PoliticaEntradaInvalida.Preservar,
        "anular" => PoliticaEntradaInvalida.Anular,
        "falhar" => PoliticaEntradaInvalida.Falhar,
        _ => throw new InvalidDataException("Política desconhecida."),
    };

    private static PoliticaEntradaMalformada Malformada(JsonElement objeto) => objeto.GetProperty("entradaMalformada").GetString() switch
    {
        "preservar" => PoliticaEntradaMalformada.Preservar,
        "anular" => PoliticaEntradaMalformada.Anular,
        "falhar" => PoliticaEntradaMalformada.Falhar,
        _ => throw new InvalidDataException("Política desconhecida."),
    };
}
