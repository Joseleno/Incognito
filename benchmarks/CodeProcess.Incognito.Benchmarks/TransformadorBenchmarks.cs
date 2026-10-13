using System.Globalization;
using BenchmarkDotNet.Attributes;
using CodeProcess.Incognito.Documentos;
using CodeProcess.Incognito.Geracao;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Benchmarks;

/// <summary>
/// Cada transformador por valor, com as entradas geradas antes pelo <see cref="GeradorBr"/> com semente fixa (a medida não
/// inclui gerar a entrada). Os pseudonimizadores internos com as políticas padrão das colunas; a fachada à parte.
/// </summary>
[MemoryDiagnoser]
public class TransformadorBenchmarks : IDisposable
{
    private readonly List<IDisposable> _descartaveis = [];
    private ChaveIncognito? _chave;
    private PseudonimizadorCpf? _cpf;
    private PseudonimizadorCnpj? _cnpj;
    private PseudonimizadorCnpj? _cnpjComOrdem;
    private PseudonimizadorTelefone? _telefone;
    private PseudonimizadorEmail? _email;
    private PseudonimizadorCep? _cep;
    private PseudonimizadorIdentificador? _identificador;
    private PseudonimizadorChavePix? _chavePix;
    private PseudonimizadorChaveDfe? _chaveDfe;
    private Pseudonimizador? _fachada;
    private string[] _cpfs = [];
    private long[] _cpfsNumericos = [];
    private string[] _cnpjsNumericos = [];
    private string[] _cnpjsAlfanumericos = [];
    private string[] _telefones = [];
    private string[] _emails = [];
    private string[] _ceps = [];
    private long[] _identificadores = [];
    private string[] _chavesPix = [];
    private string[] _chavesNfe = [];
    private string[] _chavesCte = [];
    private string[] _chavesNf3e = [];
    private string[] _chavesNff = [];

    [GlobalSetup]
    public void Preparar()
    {
        _chave = ChaveIncognito.Gerar();
        const string Ambiente = "homologacao";
        _cpf = Guardar(new PseudonimizadorCpf(_chave, "cpf", 1, Ambiente, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
        _cnpj = Guardar(new PseudonimizadorCnpj(_chave, "cnpj", 1, Ambiente, false, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
        _cnpjComOrdem = Guardar(new PseudonimizadorCnpj(_chave, "cnpj", 1, Ambiente, true, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
        _telefone = Guardar(new PseudonimizadorTelefone(_chave, "telefone", 1, Ambiente, PoliticaEntradaMalformada.Falhar));
        _email = Guardar(new PseudonimizadorEmail(_chave, "email", 1, Ambiente, PoliticaEntradaMalformada.Falhar));
        _cep = Guardar(new PseudonimizadorCep(_chave, "cep", 1, Ambiente, PoliticaEntradaMalformada.Falhar));
        _identificador = Guardar(new PseudonimizadorIdentificador(_chave, "pedido-id", 1, Ambiente, 1_000_000_000, PoliticaEntradaMalformada.Falhar));
        _chavePix = Guardar(new PseudonimizadorChavePix(_chave, "chave-pix", 1, Ambiente, _cpf, _cnpj, _email, _telefone, PoliticaEntradaMalformada.Falhar));
        _chaveDfe = Guardar(new PseudonimizadorChaveDfe(_chave, 1, Ambiente, _cpf, _cnpj, null, PoliticaEntradaInvalida.Preservar, PoliticaEntradaMalformada.Falhar));
        _fachada = Guardar(new Pseudonimizador(_chave, Ambiente));

        var gerador = new GeradorBr(Lote.Semente);
        _cpfs = Gerar(() => gerador.Cpf());
        _cpfsNumericos = [.. _cpfs.Select(cpf => long.Parse(cpf, CultureInfo.InvariantCulture))];
        _cnpjsNumericos = Gerar(() => gerador.Cnpj());
        _cnpjsAlfanumericos = Gerar(() => gerador.Cnpj(alfanumerico: true));
        _telefones = Gerar(() => gerador.Telefone(celular: true, formatado: true));
        _emails = Gerar(gerador.Email);
        _ceps = Gerar(() => gerador.Cep());
        _identificadores = [.. Enumerable.Range(1, Lote.Tamanho).Select(i => (long)i)];
        var tipos = Enum.GetValues<TipoChavePix>();
        var tipo = 0;
        _chavesPix = Gerar(() => gerador.ChavePix(tipos[tipo++ % tipos.Length]));
        _chavesNfe = Gerar(() => gerador.ChaveDfe(55));
        _chavesCte = Gerar(() => gerador.ChaveDfe(57));
        _chavesNf3e = Gerar(() => gerador.ChaveDfe(66));
        var dia = 0;
        _chavesNff = Gerar(() => Nff(gerador.ChaveDfe(55, alfanumerico: false), 1 + (dia++ % 31)));

        // Toda entrada precisa sair pseudonimizada (e a chave de DF-e sem ocorrência): um ponto fixo, um DV preservado ou
        // um serviço mediria um atalho sem acusar nada.
        Exigir(_cpfs, (string x, out string? y) => _cpf.Pseudonimizar(x, out y));
        Exigir(_cnpjsNumericos, (string x, out string? y) => _cnpj.Pseudonimizar(x, out y));
        Exigir(_cnpjsAlfanumericos, (string x, out string? y) => _cnpj.Pseudonimizar(x, out y));
        Exigir(_cnpjsNumericos, (string x, out string? y) => _cnpjComOrdem.Pseudonimizar(x, out y));
        Exigir(_telefones, (string x, out string? y) => _telefone.Pseudonimizar(x, out y));
        Exigir(_emails, (string x, out string? y) => _email.Pseudonimizar(x, out y));
        Exigir(_ceps, (string x, out string? y) => _cep.Pseudonimizar(x, "pessoa-1", out y));
        Exigir(_chavesPix, (string x, out string? y) => _chavePix.Pseudonimizar(x, out y));
        foreach (var chaves in new[] { _chavesNfe, _chavesCte, _chavesNf3e, _chavesNff })
        {
            Exigir(chaves, (string x, out string? y) =>
            {
                var desfecho = _chaveDfe.Pseudonimizar(x, out y, out var ocorrencias);
                return ocorrencias == OcorrenciasChaveDfe.Nenhuma ? desfecho : Desfecho.CopiadoEmClaro;
            });
        }

        if (_cpfsNumericos.Any(x => _cpf.Pseudonimizar(x, out _) != Desfecho.Pseudonimizado)
            || _identificadores.Any(x => _identificador.Pseudonimizar(x, out _) != Desfecho.Pseudonimizado))
        {
            throw new InvalidOperationException("Uma entrada numérica do benchmark não sai pseudonimizada.");
        }
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int Cpf() => Aplicar(_cpfs, (string x, out string? y) => _cpf!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public long CpfNumerico()
    {
        var soma = 0L;
        foreach (var x in _cpfsNumericos)
        {
            _cpf!.Pseudonimizar(x, out var y);
            soma += y ?? 0;
        }

        return soma;
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int CnpjNumerico() => Aplicar(_cnpjsNumericos, (string x, out string? y) => _cnpj!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int CnpjAlfanumerico() => Aplicar(_cnpjsAlfanumericos, (string x, out string? y) => _cnpj!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int CnpjComOrdemPermutada() => Aplicar(_cnpjsNumericos, (string x, out string? y) => _cnpjComOrdem!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int Telefone() => Aplicar(_telefones, (string x, out string? y) => _telefone!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int Email() => Aplicar(_emails, (string x, out string? y) => _email!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int Cep() => Aplicar(_ceps, (string x, out string? y) => _cep!.Pseudonimizar(x, "pessoa-1", out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public long Identificador()
    {
        var soma = 0L;
        foreach (var x in _identificadores)
        {
            _identificador!.Pseudonimizar(x, out var y);
            soma += y ?? 0;
        }

        return soma;
    }

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ChavePix() => Aplicar(_chavesPix, (string x, out string? y) => _chavePix!.Pseudonimizar(x, out y));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ChaveDfeNfe() => Aplicar(_chavesNfe, (string x, out string? y) => _chaveDfe!.Pseudonimizar(x, out y, out _));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ChaveDfeCte() => Aplicar(_chavesCte, (string x, out string? y) => _chaveDfe!.Pseudonimizar(x, out y, out _));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ChaveDfeNf3e() => Aplicar(_chavesNf3e, (string x, out string? y) => _chaveDfe!.Pseudonimizar(x, out y, out _));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int ChaveDfeNff() => Aplicar(_chavesNff, (string x, out string? y) => _chaveDfe!.Pseudonimizar(x, out y, out _));

    [Benchmark(OperationsPerInvoke = Lote.Tamanho)]
    public int FachadaCpf()
    {
        var soma = 0;
        foreach (var x in _cpfs)
        {
            soma += _fachada!.Cpf(x).Length;
        }

        return soma;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        for (var i = _descartaveis.Count - 1; i >= 0; i--)
        {
            _descartaveis[i].Dispose();
        }

        _descartaveis.Clear();
        _chave?.Dispose();
        GC.SuppressFinalize(this);
    }

    private delegate Desfecho Transformar(string entrada, out string? saida);

    private static int Aplicar(string[] entradas, Transformar transformar)
    {
        var soma = 0;
        foreach (var x in entradas)
        {
            transformar(x, out var y);
            soma += y?.Length ?? 0;
        }

        return soma;
    }

    private static void Exigir(string[] entradas, Transformar transformar)
    {
        foreach (var x in entradas)
        {
            if (transformar(x, out _) != Desfecho.Pseudonimizado)
            {
                throw new InvalidOperationException("Uma entrada do benchmark não sai pseudonimizada.");
            }
        }
    }

    private static string[] Gerar(Func<string> gerar) => [.. Enumerable.Range(0, Lote.Tamanho).Select(_ => gerar())];

    // A chave da NF-e com as partes da NFF: AAMM 2405, série 024 (on-line, ano 24), tpEmis 3, número com o dia, o mês 05,
    // o tipo 1 (CNPJ) e um sequencial; cDV recalculado.
    private static string Nff(string chave, int dia)
    {
        var numero = dia.ToString("00", CultureInfo.InvariantCulture) + "051" + (dia * 37).ToString("0000", CultureInfo.InvariantCulture);
        var primeiros43 = string.Concat(chave.AsSpan(0, 2), "2405", chave.AsSpan(6, 16), "024") + numero + "3" + chave[35..43];
        return primeiros43 + (char)('0' + DigitosVerificadores.ChaveDfe(primeiros43));
    }

    private T Guardar<T>(T descartavel)
        where T : IDisposable
    {
        _descartaveis.Add(descartavel);
        return descartavel;
    }
}
