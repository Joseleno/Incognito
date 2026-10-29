using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Dialetos.PostgreSql;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Interno;
using CodeProcess.Incognito.Testes.Conformidade;

namespace CodeProcess.Incognito.Testes.PostgreSql;

/// <summary>
/// A proteção e o preparo do destino contra o servidor, com o dialeto PostgreSQL: o destino sem marcador, com o nome errado
/// ou trocado com a origem é recusado (código 2); o <c>preparar-destino</c> grava o marcador uma vez; a segunda execução
/// simultânea sai com código 1; a origem e o destino no mesmo servidor avisam pelo endereço; a tabela ignorada com linhas
/// recusa.
/// </summary>
/// <param name="servidor">O servidor (fixture de assembly).</param>
public sealed class ProtecaoPostgreSqlTestes(ServidorPostgreSql servidor)
{
    private readonly ProvedorPostgreSql _provedor = new();

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_destino_preparado_passa_uma_execucao_por_vez_e_o_resto_e_recusado()
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.ClonarModeloAsync(cancelamento);
        var destino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await servidor.ExecutarAsync(
                destino,
                $"CREATE TABLE public.ignorada (id integer); INSERT INTO public.ignorada VALUES (1); ALTER TABLE public.ignorada OWNER TO {ServidorPostgreSql.Dono}; DROP SCHEMA incognito CASCADE;",
                cancelamento);
            var ignorada = new NomeObjeto("public", "ignorada");

            await using var daOrigem = await ConectarAsync(origem, LadoBanco.Origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor);
            await using var doDestino = await ConectarAsync(destino, LadoBanco.Destino, ServidorPostgreSql.Dono, servidor.SenhaDono);

            // C1: sem o marcador, e com o nome errado.
            var semMarcador = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [ignorada], false, Confirmar, cancelamento));
            Assert.Equal(["INC3600"], semMarcador.Erros.Select(erro => erro.Codigo));

            // O preparo grava o marcador e cria o diário uma vez; a segunda é idempotente.
            var preparo = await PreparadorDoDestino.PrepararAsync(doDestino, daOrigem, destino, Confirmar, TimeProvider.System, cancelamento);
            Assert.Equal(SituacaoDoPreparo.Gravado, preparo.Situacao);
            Assert.Equal("INC3582", Assert.Single(preparo.Avisos).Codigo);
            var denovo = await PreparadorDoDestino.PrepararAsync(doDestino, daOrigem, destino, Confirmar, TimeProvider.System, cancelamento);
            Assert.Equal((SituacaoDoPreparo.JaPreparado, preparo.Identificador), (denovo.Situacao, denovo.Identificador));

            var nomeErrado = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino + "_outro", [], false, Confirmar, cancelamento));
            Assert.Equal(["INC3603"], nomeErrado.Erros.Select(erro => erro.Codigo));

            // A tabela ignorada com linha recusa; aceitando os dados existentes, passa.
            var comLinhas = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [ignorada], false, Confirmar, cancelamento));
            Assert.Equal(("INC3604", "public.ignorada"), (Assert.Single(comLinhas.Erros).Codigo, comLinhas.Erros[0].Tabela?.ToString()));

            // C2 e C3: no mesmo servidor, o aviso pelo endereço; com o bloqueio na mão, a segunda execução sai com código 1.
            var protecao = await ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [ignorada], true, Confirmar, cancelamento);
            await using (protecao.Concessao)
            {
                Assert.Contains("o mesmo endereço", Assert.Single(protecao.Avisos).Mensagem, StringComparison.Ordinal);
                await using var outraOrigem = await ConectarAsync(origem, LadoBanco.Origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor);
                await using var outroDestino = await ConectarAsync(destino, LadoBanco.Destino, ServidorPostgreSql.Dono, servidor.SenhaDono);

                var ocupado = await Assert.ThrowsAsync<BloqueioDestinoException>(() => ProtecaoDoDestino.ConferirAsync(outraOrigem, outroDestino, destino, [ignorada], true, Confirmar, cancelamento));

                Assert.Equal("INC3004", ocupado.Codigo);
                Assert.Equal(1, ClassificadorDeSaida.De(ocupado));
                await protecao.Concessao!.ConferirAsync(cancelamento);
            }

            // C1: as conexões trocadas falham nos dois sentidos (o destino marcado não é origem; a origem não é destino).
            await using var trocadaOrigem = await ConectarAsync(destino, LadoBanco.Origem, ServidorPostgreSql.Dono, servidor.SenhaDono);
            await using var trocadoDestino = await ConectarAsync(origem, LadoBanco.Destino, ServidorPostgreSql.Leitor, servidor.SenhaLeitor);
            var trocadas = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ProtecaoDoDestino.ConferirAsync(trocadaOrigem, trocadoDestino, origem, [], false, Confirmar, cancelamento));
            Assert.Equal(["INC3600", "INC3602"], trocadas.Erros.Select(erro => erro.Codigo));
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_esquema_incognito_de_outro_dono_deixa_o_destino_marcado_sem_o_diario_ate_ser_apagado()
    {
        // O clone do esquema de conformidade traz um esquema incognito do administrador: o diário nunca o usa.
        var cancelamento = TestContext.Current.CancellationToken;
        var origem = await servidor.ClonarModeloAsync(cancelamento);
        var destino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var daOrigem = await ConectarAsync(origem, LadoBanco.Origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor);
            await using var doDestino = await ConectarAsync(destino, LadoBanco.Destino, ServidorPostgreSql.Dono, servidor.SenhaDono);

            var alheio = await Assert.ThrowsAsync<FalhaDialetoException>(() => PreparadorDoDestino.PrepararAsync(doDestino, daOrigem, destino, Confirmar, TimeProvider.System, cancelamento));
            Assert.Equal(("INC3116", 1), (alheio.Codigo, ClassificadorDeSaida.De(alheio)));
            var marcador = (await doDestino.LerIdentidadeAsync(cancelamento)).Marcador;
            Assert.Equal(SituacaoMarcador.Valido, marcador?.Situacao);

            // Com o esquema alheio, a proteção também falha; apagado ele, sem o diário, recusa; e o preparo seguinte só cria
            // o diário.
            var comAlheio = await Assert.ThrowsAsync<FalhaDialetoException>(() => ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [], false, Confirmar, cancelamento));
            Assert.Equal("INC3116", comAlheio.Codigo);
            await servidor.ExecutarAsync(destino, "DROP SCHEMA incognito CASCADE", cancelamento);
            var semDiario = await Assert.ThrowsAsync<PlanoRecusadoException>(() => ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [], false, Confirmar, cancelamento));
            Assert.Equal(["INC3607"], semDiario.Erros.Select(erro => erro.Codigo));
            var preparo = await PreparadorDoDestino.PrepararAsync(doDestino, daOrigem, destino, Confirmar, TimeProvider.System, cancelamento);

            Assert.Equal((SituacaoDoPreparo.DiarioCriado, marcador!.Identificador), (preparo.Situacao, preparo.Identificador));
            var protecao = await ProtecaoDoDestino.ConferirAsync(daOrigem, doDestino, destino, [], false, Confirmar, cancelamento);
            await using (protecao.Concessao)
            {
                Assert.Equal(marcador.Identificador, protecao.Marcador);
            }
        }
        finally
        {
            await servidor.ApagarAsync(origem);
            await servidor.ApagarAsync(destino);
        }
    }

    [Fact]
    [Trait(CategoriaDaSuite.Nome, CategoriaDaSuite.Valor)]
    public async Task O_preparo_recusa_o_destino_que_e_a_propria_origem_sem_gravar()
    {
        // A mesma linhagem lida (o cluster e o oid) no mesmo endereço; um clone no mesmo servidor só avisa.
        var cancelamento = TestContext.Current.CancellationToken;
        var destino = await servidor.CriarDestinoAsync(cancelamento);
        try
        {
            await using var mesmaOrigem = await ConectarAsync(destino, LadoBanco.Origem, ServidorPostgreSql.Leitor, servidor.SenhaLeitor);
            await using var doDestino = await ConectarAsync(destino, LadoBanco.Destino, ServidorPostgreSql.Dono, servidor.SenhaDono);

            var recusa = await Assert.ThrowsAsync<PlanoRecusadoException>(() => PreparadorDoDestino.PrepararAsync(doDestino, mesmaOrigem, destino, Confirmar, TimeProvider.System, cancelamento));

            Assert.Equal("INC3606", Assert.Single(recusa.Erros).Codigo);
            Assert.Equal(SituacaoMarcador.Ausente, (await doDestino.LerIdentidadeAsync(cancelamento)).Marcador?.Situacao);
        }
        finally
        {
            await servidor.ApagarAsync(destino);
        }
    }

    private static Task<bool> Confirmar(ResumoDoDestino resumo, CancellationToken cancelamento) => Task.FromResult(true);

    private Task<DialetoProtegido> ConectarAsync(string banco, LadoBanco lado, string usuario, string senha) =>
        DialetoProtegido.ConectarAsync(_provedor, "postgresql", lado, new SegredoConexao(servidor.StringDeConexao(banco, usuario, senha)), TestContext.Current.CancellationToken);
}
