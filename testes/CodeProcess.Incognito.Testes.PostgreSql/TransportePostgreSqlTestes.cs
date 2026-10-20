using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Testes.Conformidade;
using Npgsql;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// O transporte da conexão, sem servidor: o que é a própria máquina, quando o TLS só preferido sobe para obrigatório e o
/// que o modo de TLS e o servidor dizem da cifra e da verificação. A fixture só exercita o loopback sem TLS (pela suíte).
/// </summary>
public sealed class TransportePostgreSqlTestes
{
    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("localhost", true)]
    [InlineData("LocalHost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.10.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("/var/run/postgresql", true)]
    [InlineData("127.0.0.1:5433", true)]
    [InlineData("localhost:5433", true)]
    [InlineData("[::1]:5433", true)]
    [InlineData("127.exemplo.invalid", false)]
    [InlineData("127.exemplo.invalid:5433", false)]
    [InlineData("localhost.exemplo.invalid", false)]
    [InlineData("db.exemplo.invalid", false)]
    [InlineData("10.0.0.1", false)]
    public void A_propria_maquina_e_o_loopback_o_localhost_ou_o_socket_nunca_um_prefixo_do_nome(string servidor, bool local) =>
        Assert.Equal(local, ConexaoPostgreSql.Local(servidor));

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData("127.0.0.1", SslMode.Prefer)]
    [InlineData("127.0.0.1:5433", SslMode.Prefer)]
    [InlineData("/var/run/postgresql", SslMode.Prefer)]
    [InlineData("127.exemplo.invalid", SslMode.Require)]
    [InlineData("db.exemplo.invalid", SslMode.Require)]
    public void O_tls_preferido_so_fica_preferido_na_propria_maquina(string servidor, SslMode imposto)
    {
        var construtor = new NpgsqlConnectionStringBuilder { Host = servidor, Database = "loja", Password = "x" };

        ConexaoPostgreSql.Impor(construtor, origem: true);

        Assert.Equal(imposto, construtor.SslMode);
    }

    [Theory]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    [InlineData(SslMode.Disable, "db.exemplo.invalid", false, false, false)]
    [InlineData(SslMode.Allow, "db.exemplo.invalid", true, null, false)]
    [InlineData(SslMode.Prefer, "127.0.0.1", true, null, false)]
    [InlineData(SslMode.Prefer, "127.0.0.1", false, false, false)]
    [InlineData(SslMode.Require, "db.exemplo.invalid", true, true, false)]
    [InlineData(SslMode.Require, "db.exemplo.invalid", null, true, false)]
    [InlineData(SslMode.Require, "/var/run/postgresql", false, false, false)]
    [InlineData(SslMode.Require, "/var/run/postgresql", null, false, false)]
    [InlineData(SslMode.Prefer, "/var/run/postgresql", true, false, false)]
    [InlineData(SslMode.VerifyCA, "db.exemplo.invalid", true, true, false)]
    [InlineData(SslMode.VerifyFull, "db.exemplo.invalid", true, true, true)]
    [InlineData(SslMode.VerifyFull, "db.exemplo.invalid", false, false, false)]
    [InlineData(SslMode.VerifyFull, "/var/run/postgresql", null, false, false)]
    public void A_cifra_sai_do_modo_de_tls_e_o_servidor_so_a_rebaixa(SslMode modo, string servidor, bool? cifradaNoServidor, bool? cifrado, bool verificado)
    {
        var transporte = ConexaoPostgreSql.Transporte(modo, servidor, cifradaNoServidor);

        Assert.Equal((cifrado, verificado, ConexaoPostgreSql.Local(servidor)), (transporte.Cifrado, transporte.ServidorVerificado, transporte.Local));
    }
}
