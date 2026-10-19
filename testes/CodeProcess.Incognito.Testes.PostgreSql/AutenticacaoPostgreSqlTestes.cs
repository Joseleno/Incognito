using System.Net;
using System.Net.Sockets;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O que o dialeto nunca manda a um servidor hostil ou a alguém no meio, provado contra um servidor falso: a senha em
/// claro, a credencial do usuário do sistema (SSPI) quando a string tem senha, e o pacote de início sem TLS fora da
/// própria máquina. E a senha do ambiente, que nunca vai no lugar da que falta na string.
/// </summary>
public sealed class AutenticacaoPostgreSqlTestes
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(ServidorFalsoPostgreSql.SenhaEmClaro)]
    [InlineData(ServidorFalsoPostgreSql.Sspi)]
    public async Task O_servidor_que_pede_senha_em_claro_ou_SSPI_nao_recebe_credencial(int autenticacao)
    {
        await using var falso = new ServidorFalsoPostgreSql(IPAddress.Loopback, autenticacao);

        await Assert.ThrowsAnyAsync<Exception>(() => _provedor.ConectarOrigemAsync(Conexao("127.0.0.1", falso.Porta), TestContext.Current.CancellationToken));

        var recebido = await falso.Recebido;
        Assert.Contains("inicio", recebido);
        Assert.DoesNotContain("p", recebido);
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task Fora_da_propria_maquina_a_sessao_nunca_segue_sem_TLS()
    {
        // Um endereço da máquina que não é loopback: para o dialeto, um servidor remoto.
        var endereco = (await Dns.GetHostAddressesAsync(Dns.GetHostName(), TestContext.Current.CancellationToken))
            .FirstOrDefault(lido => lido.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(lido));
        Assert.SkipWhen(endereco is null, "A máquina não tem endereço IPv4 fora do loopback.");
        await using var falso = new ServidorFalsoPostgreSql(endereco!, ServidorFalsoPostgreSql.SenhaEmClaro);

        await Assert.ThrowsAnyAsync<Exception>(() => _provedor.ConectarOrigemAsync(Conexao(endereco!.ToString(), falso.Porta), TestContext.Current.CancellationToken));

        var recebido = await falso.Recebido;
        Assert.Contains("tls", recebido);
        Assert.DoesNotContain("inicio", recebido);
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("PGPASSWORD")]
    [InlineData("PGPASSFILE")]
    public async Task String_sem_senha_com_senha_no_ambiente_e_recusada(string variavel)
    {
        var antes = Environment.GetEnvironmentVariable(variavel);
        Environment.SetEnvironmentVariable(variavel, ServidorPostgreSql.NovaSenha());
        try
        {
            var erro = await Assert.ThrowsAsync<IncognitoException>(() => _provedor.ConectarDestinoAsync(
                new SegredoConexao("Host=127.0.0.1;Database=loja;Username=u"), TestContext.Current.CancellationToken));

            Assert.Equal("INC3102", erro.Codigo);
            Assert.DoesNotContain(ServidorPostgreSql.Sentinela, erro.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variavel, antes);
        }
    }

    private static SegredoConexao Conexao(string servidor, int porta) =>
        new($"Host={servidor};Port={porta};Database=loja;Username=u;Password={ServidorPostgreSql.NovaSenha()};Timeout=10");
}
