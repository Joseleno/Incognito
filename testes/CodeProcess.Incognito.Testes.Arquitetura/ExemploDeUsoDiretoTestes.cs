namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// O exemplo de uso direto da documentação, escrito como quem usa o pacote: este projeto não enxerga os tipos internos do
/// núcleo, então o exemplo só compila com a API pública. Roda sozinho, porque mexe numa variável de ambiente do
/// processo, e devolve o valor que ela tinha.
/// </summary>
[Collection(nameof(ExemploDeUsoDiretoTestes))]
public sealed class ExemploDeUsoDiretoTestes
{
    private const string VariavelDaChave = "INCOGNITO_CHAVE";

    [Fact]
    public void Exemplo_de_uso_direto_compila_e_roda()
    {
        // A chave do exemplo vem de uma variável de ambiente: aqui, gerada na hora e exportada para ela.
        var anterior = Environment.GetEnvironmentVariable(VariavelDaChave);
        using (var gerada = ChaveIncognito.Gerar())
        {
            Span<char> texto = stackalloc char[64];
            Assert.True(gerada.TentarExportar(texto, out var escritos));
            Environment.SetEnvironmentVariable(VariavelDaChave, new string(texto[..escritos]));
            texto.Clear();
        }

        try
        {
            // Geração de dados de teste, com semente opcional para reprodutibilidade
            string cpf = GeradorBr.Padrao.Cpf(formatado: true);
            var gerador = new GeradorBr(semente: 42);
            string cnpj = gerador.Cnpj(alfanumerico: true);

            // Validação (sobrecargas com ReadOnlySpan<char> para evitar alocação)
            bool valido = ValidadorBr.Cpf(cpf);
            bool chaveValida = ValidadorBr.ChaveDfe(gerador.ChaveDfe(modelo: 55));

            // Pseudonimização: mesma chave, ambiente e domínio, mesmo resultado; thread-safe. A chave vem de uma variável
            // de ambiente ou de um arquivo, nunca do código, e precisa viver enquanto o pseudonimizador for usado.
            using var chave = ChaveIncognito.Ler(Environment.GetEnvironmentVariable("INCOGNITO_CHAVE"));
            using var pseudonimizador = new Pseudonimizador(chave, ambiente: "homologacao");
            string pseudonimo = pseudonimizador.Cpf(cpf);                   // domínio padrão cpf@1; continua sendo dado pessoal

            Assert.True(valido, "O CPF gerado não é válido.");
            Assert.True(chaveValida, "A chave de DF-e gerada não é válida.");
            Assert.True(ValidadorBr.Cnpj(cnpj), "O CNPJ gerado não é válido.");
            Assert.True(ValidadorBr.Cpf(pseudonimo), "O pseudônimo não é um CPF válido.");
            Assert.True(pseudonimo != cpf && pseudonimo.Length == cpf.Length, "O pseudônimo não está na mesma forma ou não mudou.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(VariavelDaChave, anterior);
        }
    }
}

[CollectionDefinition(nameof(ExemploDeUsoDiretoTestes), DisableParallelization = true)]
public sealed class ColecaoDoExemploDeUsoDireto;
