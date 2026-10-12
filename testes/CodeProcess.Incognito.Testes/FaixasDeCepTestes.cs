using CodeProcess.Incognito.Documentos;
using static CodeProcess.Incognito.Testes.DocumentosDeTeste;

namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Faixas de CEP compartilhadas pela pseudonimização e pela geração: ordem, sobreposição, contagem por UF e a tabela
/// escrita à parte nos testes.
/// </summary>
public sealed class FaixasDeCepTestes
{
    [Fact]
    public void Faixas_de_saida_sao_crescentes_disjuntas_e_cobrem_de_01000_000_a_99999_999_menos_a_antiga_de_RO()
    {
        var faixas = FaixasDeCep.DeSaida.ToArray();

        Assert.True(faixas.All(faixa => faixa.Inicio <= faixa.Fim), "Uma faixa termina antes de começar.");
        Assert.True(faixas.Zip(faixas.Skip(1)).All(par => par.First.Fim < par.Second.Inicio), "As faixas de saída se sobrepõem ou saem de ordem.");
        Assert.Equal(1_000_000, faixas[0].Inicio);
        Assert.Equal(99_999_999, faixas[^1].Fim);
        var buracos = faixas.Zip(faixas.Skip(1)).Where(par => par.Second.Inicio != par.First.Fim + 1).Select(par => (par.First.Fim + 1, par.Second.Inicio - 1)).ToArray();
        Assert.Equal([(78_900_000, 78_999_999)], buracos);
    }

    [Fact]
    public void Faixa_so_de_entrada_e_a_antiga_de_RO_e_nao_cruza_as_de_saida()
    {
        var entrada = FaixasDeCep.SoDeEntrada.ToArray();

        Assert.Single(entrada);
        Assert.Equal("RO", FaixasDeCep.Ufs[entrada[0].Uf]);
        Assert.Equal(-1, FaixasDeCep.UfDe(entrada[0].Inicio, FaixasDeCep.DeSaida));
        Assert.Equal(-1, FaixasDeCep.UfDe(entrada[0].Fim, FaixasDeCep.DeSaida));
    }

    [Fact]
    public void Tabela_bate_com_a_escrita_a_parte_e_cada_UF_tem_CEPs()
    {
        Assert.Equal(27, FaixasDeCep.Ufs.Length);
        for (var uf = 0; uf < FaixasDeCep.Ufs.Length; uf++)
        {
            var indice = uf;
            var daUf = FaixasDeCep.DeSaida.ToArray().Where(faixa => faixa.Uf == indice).Select(faixa => (faixa.Inicio, faixa.Fim)).ToArray();
            var esperado = FaixasDeCepPorUf[FaixasDeCep.Ufs[uf]];

            Assert.Equal(esperado, daUf);
            Assert.Equal((ulong)esperado.Sum(faixa => (long)faixa.Fim - faixa.Inicio + 1), FaixasDeCep.CepsPorUf[uf]);
        }

        Assert.Equal(99_000_000UL - 100_000UL, FaixasDeCep.CepsPorUf.ToArray().Aggregate(0UL, (soma, quantidade) => soma + quantidade));
    }

    [Fact]
    public void CEP_na_posicao_percorre_as_faixas_da_UF_em_ordem()
    {
        // DF tem duas faixas: a posição igual ao tamanho da primeira cai no início da segunda.
        var df = Array.IndexOf(FaixasDeCep.Ufs.ToArray(), "DF");

        Assert.Equal(70_000_000, FaixasDeCep.CepNaPosicao(df, 0));
        Assert.Equal(72_799_999, FaixasDeCep.CepNaPosicao(df, 2_799_999));
        Assert.Equal(73_000_000, FaixasDeCep.CepNaPosicao(df, 2_800_000));
        Assert.Equal(73_699_999, FaixasDeCep.CepNaPosicao(df, FaixasDeCep.CepsPorUf[df] - 1));
    }
}
