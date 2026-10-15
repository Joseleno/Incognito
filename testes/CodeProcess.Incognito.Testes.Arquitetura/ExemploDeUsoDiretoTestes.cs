namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// Os exemplos de uso direto do README, escritos como quem usa o pacote: este projeto não enxerga os tipos internos do
/// núcleo, então os exemplos só compilam com a API pública. O trecho entre os marcadores é igual, linha a linha, ao bloco
/// de código do README (<see cref="ReadmeTestes"/>). Rodam sozinhos, porque mexem na variável de ambiente e no diretório
/// atual do processo, e devolvem o que encontraram.
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
            // início do exemplo do README
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
            // fim do exemplo do README

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

    [Fact]
    public void Criar_uma_chave_grava_um_arquivo_novo_que_a_leitura_aceita()
    {
        // O trecho grava "incognito.chave" no diretório atual: aqui, um diretório temporário, apagado no fim.
        var anterior = Environment.CurrentDirectory;
        var pasta = Directory.CreateTempSubdirectory("incognito-chave-");
        Environment.CurrentDirectory = pasta.FullName;
        try
        {
            // início da criação da chave do README
            // Gera uma chave nova e grava num arquivo novo, que no Linux e no macOS só o seu usuário lê. Guarde o arquivo fora
            // do repositório e do ambiente de destino: quem tem a chave reverte todos os pseudônimos.
            using var nova = ChaveIncognito.Gerar();
            Span<char> texto = stackalloc char[48];
            if (!nova.TentarExportar(texto, out int escritos))
            {
                throw new InvalidOperationException("Espaço insuficiente para a chave.");
            }

            // Sem buffer intermediário, e os bytes zerados depois: a chave em claro só fica no arquivo.
            var opcoes = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, BufferSize = 0 };
            if (!OperatingSystem.IsWindows())
            {
                opcoes.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            Span<byte> bytes = stackalloc byte[48];
            int gravados = System.Text.Encoding.ASCII.GetBytes(texto[..escritos], bytes);
            using (var arquivo = new FileStream("incognito.chave", opcoes))
            {
                arquivo.Write(bytes[..gravados]);
            }

            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            texto.Clear();
            Console.WriteLine($"Chave gravada em incognito.chave. Impressão digital: {nova.ImpressaoDigital}");
            // fim da criação da chave do README

            var caminho = Path.Combine(pasta.FullName, "incognito.chave");
            using (var lida = ChaveIncognito.Ler(File.ReadAllText(caminho)))
            {
                Assert.Equal(nova.ImpressaoDigital, lida.ImpressaoDigital);
            }

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(caminho));
            }

            // O arquivo novo nunca sobrescreve uma chave que já existe.
            Assert.Throws<IOException>(() => new StreamWriter("incognito.chave", opcoes).Dispose());
        }
        finally
        {
            Environment.CurrentDirectory = anterior;
            pasta.Delete(recursive: true);
        }
    }
}

[CollectionDefinition(nameof(ExemploDeUsoDiretoTestes), DisableParallelization = true)]
public sealed class ColecaoDoExemploDeUsoDireto;
