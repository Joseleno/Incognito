using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Testes.Motor.Dialetos;

/// <summary>
/// Os tipos do contrato de dialeto: o segredo da conexão nunca aparece, os nomes comparam sem ignorar a caixa, o modelo
/// do esquema é imutável e recusa nulos, e o descarte do dialeto roda uma vez só.
/// </summary>
public sealed class ContratoDeDialetoTestes
{
    private const string Sentinela = "Host=servidor;Password=SENTINELA-SENHA-123";

    [Fact]
    public void O_segredo_da_conexao_nunca_aparece_no_texto()
    {
        var segredo = new SegredoConexao(Sentinela);

        Assert.Equal(Sentinela, segredo.Revelar());
        Assert.Equal("[segredo]", segredo.ToString());
        Assert.DoesNotContain("SENTINELA", $"{segredo}", StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new SegredoConexao(null!));
        Assert.Equal("valor", Assert.Throws<ArgumentException>(() => new SegredoConexao("   ")).ParamName);
    }

    [Fact]
    public void Os_nomes_comparam_sem_ignorar_a_caixa()
    {
        var maiuscula = new NomeObjeto("loja", "Cliente");

        Assert.Equal(new NomeObjeto("loja", "Cliente"), maiuscula);
        Assert.Equal(new NomeObjeto("loja", "Cliente").GetHashCode(), maiuscula.GetHashCode());
        Assert.NotEqual(new NomeObjeto("loja", "cliente"), maiuscula);
        Assert.NotEqual(new NomeObjeto("Loja", "Cliente"), maiuscula);
        Assert.Equal("loja.Cliente", maiuscula.ToString());
        Assert.Equal(new ReferenciaColuna(maiuscula, "Nome"), new ReferenciaColuna(new NomeObjeto("loja", "Cliente"), "Nome"));
        Assert.NotEqual(new ReferenciaColuna(maiuscula, "Nome"), new ReferenciaColuna(maiuscula, "nome"));
        Assert.Throws<ArgumentException>(() => new NomeObjeto("", "x"));
        Assert.Throws<ArgumentNullException>(() => new ReferenciaColuna(null!, "x"));
    }

    [Fact]
    public void O_modelo_copia_as_listas_e_recusa_nulos_e_valores_fora_do_enum()
    {
        var colunas = new List<ColunaEsquema> { new() { Nome = "id", TipoNativo = "bigint", Situacao = SituacaoTipo.Suportado, Tipo = TipoColuna.Inteiro } };
        var tabela = new TabelaEsquema { Nome = new NomeObjeto("loja", "itens"), Colunas = colunas };
        colunas.Clear();

        Assert.Single(tabela.Colunas);
        Assert.Throws<NotSupportedException>(() => ((IList<ColunaEsquema>)tabela.Colunas).Add(null!));
        Assert.Throws<ArgumentNullException>(() => new TabelaEsquema { Nome = new NomeObjeto("loja", "itens"), Colunas = [null!] });
        Assert.Throws<ArgumentNullException>(() => new TabelaEsquema { Nome = null!, Colunas = [] });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TabelaEsquema { Nome = new NomeObjeto("loja", "itens"), Colunas = [], Tipo = (TipoTabela)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColunaEsquema { Nome = "id", TipoNativo = "bigint", Situacao = (SituacaoTipo)99 });
        Assert.Throws<ArgumentException>(() => new ColunaEsquema { Nome = "", TipoNativo = "bigint", Situacao = SituacaoTipo.Suportado });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjetoResidual { Nome = new NomeObjeto("loja", "x"), Tipo = (TipoObjetoResidual)99 });

        Assert.Equal("ChavePrimaria", Assert.Throws<ArgumentException>(() => new TabelaEsquema { Nome = new NomeObjeto("loja", "itens"), Colunas = [], ChavePrimaria = [""] }).ParamName);
        Assert.Equal("Colunas", Assert.Throws<ArgumentNullException>(() => new TabelaEsquema { Nome = new NomeObjeto("loja", "itens"), Colunas = [null!] }).ParamName);
        Assert.Throws<ArgumentNullException>(() => new IdentidadeLida { Banco = "loja", Servidor = "", Linhagem = new Dictionary<string, string> { ["identificador"] = null! } });
        Assert.Throws<ArgumentException>(() => new IdentidadeLida { Banco = "loja", Servidor = "", Linhagem = new Dictionary<string, string> { [""] = "1" } });

        var linhagem = new Dictionary<string, string> { ["identificador"] = "1" };
        var identidade = new IdentidadeLida { Banco = "loja", Servidor = "", Linhagem = linhagem };
        linhagem.Clear();
        Assert.Single(identidade.Linhagem);
    }

    [Fact]
    public async Task O_descarte_do_dialeto_roda_uma_vez_so()
    {
        var dialeto = new OrigemContada();

        dialeto.Usar();
        await dialeto.DisposeAsync();
        await dialeto.DisposeAsync();

        Assert.Equal(1, dialeto.Descartes);
        Assert.Throws<ObjectDisposedException>(dialeto.Usar);
    }

    [Fact]
    public void O_nome_citado_junta_esquema_e_nome()
    {
        var sql = new SqlFalso(semEscape: false);

        Assert.Equal("\"loja\".\"nota fiscal\"", sql.Citar(new NomeObjeto("loja", "nota fiscal")));
        Assert.Throws<ArgumentNullException>(() => sql.Citar((NomeObjeto)null!));
    }

    private sealed class OrigemContada : DialetoOrigem
    {
        internal int Descartes { get; private set; }

        internal void Usar() => LancarSeDescartado();

        public override LeitorEsquema Esquema => throw new NotSupportedException();

        public override IdentidadeBanco Identidade => throw new NotSupportedException();

        public override DialetoSql Sql => throw new NotSupportedException();

        protected override async ValueTask DisposeAsyncCore()
        {
            Descartes++;
            await base.DisposeAsyncCore();
        }
    }
}
