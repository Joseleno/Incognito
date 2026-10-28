// O acesso, os residuais, a publicação e o banco são tipos do contrato experimental de dialeto que o motor declara; nada
// daqui é público.
#pragma warning disable INCX001

using System.Globalization;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O que as ações, o login, os objetos fora das tabelas e o banco permitem (P9): a fronteira entre ações pelas chaves
/// estrangeiras do destino, a segurança por linha e as permissões de cada lado, os objetos residuais, a publicação e a
/// assinatura do destino, e a codificação, a versão e o transporte. Sempre pela tabela que o comando nomeia (a raiz), mais as
/// triggers, a publicação e a assinatura de cada tabela da árvore. Uma permissão que não vem <c>true</c> recusa: a falha no meio da execução, depois de
/// esvaziar, sai mais cara. A unidade já recusada pela estrutura (ambígua, ou copiada sem um dos lados) não é conferida aqui:
/// os erros de permissão dela levariam a corrigir o que não é o problema.
/// </summary>
internal sealed partial class MontagemDoPlano
{
    // A ordem dos nomes nas mensagens: esquema e nome, ordinal.
    private static readonly IComparer<NomeObjeto> _ordemDosNomes = Comparer<NomeObjeto>.Create((um, outro) =>
        string.CompareOrdinal(um.Esquema, outro.Esquema) is var esquema and not 0 ? esquema : string.CompareOrdinal(um.Nome, outro.Nome));

    private readonly List<ChaveDoDiario> _chavesDoDiario = [];

    // P9: na ordem das unidades; as permissões só do lado que o dialeto informa.
    private void ConferirAcesso()
    {
        ConferirFronteira();
        var daOrigem = LerPermissoes(_origem.Esquema, "na origem");
        var doDestino = LerPermissoes(_destino.Esquema, "no destino");
        foreach (var unidade in _unidades)
        {
            var acao = _acoes[unidade].Acao;
            if (unidade.Ambigua || (acao == AcaoTabela.Copiar && (unidade.Origem is null || unidade.Destino is null)))
            {
                continue;
            }

            if (acao == AcaoTabela.Copiar && unidade.Origem is { } origem && daOrigem is not null)
            {
                ConferirLeitura(unidade, daOrigem.Tabelas.GetValueOrDefault(origem.Nome));
            }

            if (acao != AcaoTabela.Ignorar && unidade.Destino is not null)
            {
                if (doDestino is not null)
                {
                    ConferirGravacao(unidade, acao, doDestino);
                }

                ConferirPublicacao(unidade);
                ConferirAssinatura(unidade);
            }
        }

        if (doDestino is not null)
        {
            ConferirChavesDoDiario(doDestino);
            ConferirSequences(doDestino);
        }

        ConferirResiduais(doDestino);
        AvisarNaoSuportadas();
        ConferirBanco();
    }

    // A fronteira, pelas chaves estrangeiras do destino (do filho ao pai): copiar para esvaziar deixa toda linha com a chave
    // órfã; copiar e ignorar só se encontram sem coluna tratada na chave. Entram no conjunto que o diário remove as chaves com
    // o filho copiado, ou com o pai copiado ou esvaziado; a do filho esvaziado para o pai ignorado fica (o esvaziamento do
    // filho não esbarra nela).
    private void ConferirFronteira()
    {
        var porNome = _destino.Esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        foreach (var tabela in _destino.Esquema.Tabelas)
        {
            if (!_unidadeDe.TryGetValue(tabela, out var filho))
            {
                continue;
            }

            foreach (var chave in tabela.ChavesEstrangeiras)
            {
                if (!porNome.TryGetValue(chave.Referenciada, out var referenciada) || !_unidadeDe.TryGetValue(referenciada, out var pai))
                {
                    continue;
                }

                var (doFilho, doPai) = (_acoes[filho].Acao, _acoes[pai].Acao);
                if (doFilho == AcaoTabela.Copiar || doPai != AcaoTabela.Ignorar)
                {
                    _chavesDoDiario.Add(new ChaveDoDiario(tabela, chave, referenciada, filho));
                }

                // A chave declarada direto numa partição diz a partição: o nome da restrição só é único por tabela.
                var rotulo = NomeTabela.Parte(chave.Nome) + (ReferenceEquals(tabela, filho.Destino) ? "" : $" (da partição {Exibir(tabela.Nome)})");
                var outra = Exibir(pai.Nome);
                switch (doFilho, doPai)
                {
                    case (AcaoTabela.Copiar, AcaoTabela.Esvaziar):
                        _coletor.Erro(new ErroPlano(
                            CodigosPlanejar.CopiadaParaEsvaziada,
                            $"A chave estrangeira {rotulo} liga esta tabela, copiada, a {outra}, esvaziada: toda linha com a chave preenchida ficaria órfã. Copie {outra}, ou esvazie esta tabela também.",
                            filho.NomeTabela,
                            objeto: chave.Nome));
                        break;
                    case (AcaoTabela.Copiar, AcaoTabela.Ignorar) when Tratada(filho, chave.Colunas):
                        _coletor.Erro(new ErroPlano(
                            CodigosPlanejar.TratadaNaFronteira,
                            $"A chave estrangeira {rotulo} liga coluna tratada desta tabela, copiada, a {outra}, ignorada: o pseudônimo não acha o valor real que fica lá. Copie {outra} com a mesma regra, ou não trate a coluna.",
                            filho.NomeTabela,
                            objeto: chave.Nome));
                        break;
                    case (AcaoTabela.Ignorar, AcaoTabela.Copiar) when Tratada(pai, chave.ColunasReferenciadas):
                        _coletor.Erro(new ErroPlano(
                            CodigosPlanejar.TratadaNaFronteira,
                            $"A chave estrangeira {rotulo} liga esta tabela, ignorada, a coluna tratada de {outra}, copiada: o valor real que fica aqui não acha o pseudônimo de lá. Copie esta tabela, ou esvazie-a.",
                            filho.NomeTabela,
                            objeto: chave.Nome));
                        break;
                    case (AcaoTabela.Copiar, AcaoTabela.Ignorar):
                        _coletor.Aviso(new Aviso(
                            CodigosPlanejar.FronteiraComIgnorada,
                            $"A chave estrangeira {rotulo} liga esta tabela, copiada, a {outra}, ignorada: as linhas copiadas apontam para o que o destino tem lá, e a recriação da chave falha se faltar algum par.",
                            filho.NomeTabela,
                            objeto: chave.Nome));
                        break;
                    case (AcaoTabela.Ignorar, AcaoTabela.Copiar or AcaoTabela.Esvaziar):
                        _coletor.Aviso(new Aviso(
                            CodigosPlanejar.FronteiraComIgnorada,
                            $"A chave estrangeira {rotulo} liga esta tabela, ignorada, a {outra}, {(doPai == AcaoTabela.Copiar ? "copiada" : "esvaziada")}: esvaziar {outra} exige remover a chave, e a recriação falha se as linhas daqui não acharem mais o par.",
                            filho.NomeTabela,
                            objeto: chave.Nome));
                        break;
                }
            }
        }
    }

    // Se alguma das colunas do destino é tratada na unidade copiada, também por uma gerada calculada de uma tratada.
    private bool Tratada(Unidade unidade, IReadOnlyList<string> colunas) =>
        _pares.ContainsKey(unidade) && TratadasNoDestino(unidade).Overlaps(colunas);

    // As permissões de um lado, por objeto; nulas quando o dialeto não informa, e então nenhuma unidade é aprovada.
    private Permissoes? LerPermissoes(EsquemaBanco esquema, string lado)
    {
        if (esquema.Acesso is not { } acesso)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.AcessoNaoInformado,
                $"O dialeto não informa as permissões do login {lado}: sem elas, o plano não aprova a cópia."));
            return null;
        }

        return new Permissoes(
            acesso.Tabelas.DistinctBy(permissao => permissao.Tabela).ToDictionary(permissao => permissao.Tabela),
            acesso.Residuais.DistinctBy(permissao => (permissao.Nome, permissao.Tipo)).ToDictionary(permissao => (permissao.Nome, permissao.Tipo), permissao => permissao.PodeLimpar),
            acesso.Sequences.DistinctBy(permissao => permissao.Nome).ToDictionary(permissao => permissao.Nome, permissao => permissao.PodeAjustar));
    }

    // A origem copiada: a segurança por linha e a leitura de todas as colunas, pela raiz.
    private void ConferirLeitura(Unidade unidade, PermissoesTabela? permissao)
    {
        if (permissao?.LeituraFiltradaPorLinha != false)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.LeituraFiltradaPorLinha,
                "A segurança por linha vale para o login na leitura da origem, ou o dialeto não informa: a leitura sairia filtrada, sem erro, e a cópia ficaria incompleta. Leia com um login que ela não alcance (no PostgreSQL, com BYPASSRLS, ou o dono da tabela sem FORCE ROW LEVEL SECURITY).",
                unidade.NomeTabela));
        }

        if (permissao?.PodeLer != true)
        {
            var colunas = permissao?.ColunasSemLeitura ?? [];
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.SemLeitura,
                $"O login da origem não lê todas as colunas da tabela{(colunas.Count > 0 ? $" (sem leitura: {string.Join(", ", colunas.Select(NomeTabela.Parte))})" : "")}, ou o dialeto não informa: dê a leitura da tabela e do esquema dela.",
                unidade.NomeTabela));
        }
    }

    // O destino copiado ou esvaziado: a segurança por linha na gravação, o esvaziamento, a inserção, a identidade e as
    // triggers da árvore, pela raiz e por cada partição com trigger própria ligada.
    private void ConferirGravacao(Unidade unidade, AcaoTabela acao, Permissoes permissoes)
    {
        var raiz = unidade.Destino!;
        var permissao = permissoes.Tabelas.GetValueOrDefault(raiz.Nome);
        var copia = acao == AcaoTabela.Copiar;
        if (copia && permissao?.EscritaRestritaPorLinha != false)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.EscritaRestritaPorLinha,
                "A segurança por linha vale para o login na gravação do destino, ou o dialeto não informa: a carga falharia. Grave com um login que ela não alcance (no PostgreSQL, com BYPASSRLS, ou o dono da tabela sem FORCE ROW LEVEL SECURITY), ou desligue a segurança por linha no destino.",
                unidade.NomeTabela));
        }

        var faltam = new List<string>();
        if (permissao?.PodeEsvaziar != true)
        {
            faltam.Add("esvaziar a tabela");
        }

        if (copia && permissao?.PodeInserir != true)
        {
            faltam.Add("inserir nela");
        }

        if (copia && _pares.TryGetValue(unidade, out var pares) && pares.Any(par => par.Destino.Identidade == IdentidadeColuna.Sempre) && permissao?.PodeGravarIdentidade != true)
        {
            faltam.Add("gravar a identidade que só o banco gera");
        }

        if (faltam.Count > 0)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.SemGravacao,
                $"O login do destino não pode {Juntar(faltam)}, ou o dialeto não informa: dê a permissão antes de copiar.",
                unidade.NomeTabela));
        }

        var arvore = TabelasNoDestino(unidade).ToList();
        var desligada = arvore.Any(tabela => tabela.Triggers.Any(trigger => trigger.Ligada));
        if (desligada && permissao?.PodeDesligarTriggers != true)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.SemDesligarTriggers,
                "A tabela do destino, ou uma partição dela, tem trigger ligada, e o login não pode desligar as triggers da tabela, ou o dialeto não informa: a carga as dispararia. Copie com um login que possa desligá-las (no PostgreSQL, o dono da tabela).",
                unidade.NomeTabela));
        }

        // Com a árvore desligada, cada partição com trigger, ligada ou não: o diário desliga e religa cada tabela física
        // sozinha (o desligamento pela raiz só alcança as cópias das triggers dela, e a restauração exata, uma trigger
        // desligada numa partição e ligada na raiz, só se faz tabela por tabela), e só quem pode alterar a partição o faz.
        foreach (var particao in arvore.Where(tabela => desligada && !ReferenceEquals(tabela, raiz) && tabela.Triggers.Count > 0))
        {
            if (permissoes.Tabelas.GetValueOrDefault(particao.Nome)?.PodeDesligarTriggers != true)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.SemDesligarTriggers,
                    $"A partição {Exibir(particao.Nome)} do destino tem trigger, e o login não pode desligar e religar as triggers dela, ou o dialeto não informa: a cópia desliga e restaura cada tabela da árvore. Copie com um login que possa alterá-las (no PostgreSQL, o dono da partição).",
                    unidade.NomeTabela,
                    objeto: particao.Nome.Nome));
            }
        }
    }

    // A publicação do destino: o que se grava na raiz ou numa partição chega aos assinantes.
    private void ConferirPublicacao(Unidade unidade)
    {
        var publicadas = TabelasNoDestino(unidade).Where(tabela => tabela.Publicada != false).Select(tabela => tabela.Nome).ToList();
        if (publicadas.Count > 0)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.TabelaPublicada,
                $"A tabela do destino, ou uma partição dela, está numa publicação de replicação, ou o dialeto não informa ({Lista(publicadas.Order(_ordemDosNomes))}): o que o Incognito grava nela chegaria aos assinantes, fora do destino. Tire as tabelas da publicação antes de esvaziar ou copiar.",
                unidade.NomeTabela));
        }
    }

    // A assinatura do destino: o que chega da publicação assinada grava na raiz ou numa partição, ao lado dos pseudônimos.
    private void ConferirAssinatura(Unidade unidade)
    {
        var assinadas = TabelasNoDestino(unidade).Where(tabela => tabela.Assinada != false).Select(tabela => tabela.Nome).ToList();
        if (assinadas.Count > 0)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.TabelaAssinada,
                $"A tabela do destino, ou uma partição dela, está numa assinatura de replicação, ligada ou não, ou o dialeto não informa ({Lista(assinadas.Order(_ordemDosNomes))}): o que chega da publicação assinada gravaria nela ao lado do que o Incognito grava. Tire as tabelas da assinatura, ou apague a assinatura, antes de esvaziar ou copiar.",
                unidade.NomeTabela));
        }
    }

    // As chaves que o diário remove e recria: alterar as restrições no filho e referenciar o pai, e um estado que a
    // recriação devolva igual.
    private void ConferirChavesDoDiario(Permissoes permissoes)
    {
        foreach (var chave in _chavesDoDiario)
        {
            if (chave.Chave.DesligadaSemRestauracao != false)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.ChaveSemRestauracao,
                    $"A chave estrangeira {NomeTabela.Parte(chave.Chave.Nome)} do destino é removida e recriada na cópia, e a conferência dela está desligada de um jeito que o login não restaura (no PostgreSQL, triggers internas desligadas pelo superuser), ou o dialeto não informa: religue a conferência da chave antes de copiar.",
                    chave.Filho.NomeTabela,
                    objeto: chave.Chave.Nome));
            }

            var faltam = new List<string>();
            if (permissoes.Tabelas.GetValueOrDefault(chave.Tabela.Nome)?.PodeAlterarRestricoes != true)
            {
                faltam.Add($"alterar as restrições de {Exibir(chave.Tabela.Nome)}");
            }

            if (permissoes.Tabelas.GetValueOrDefault(chave.Referenciada.Nome)?.PodeReferenciar != true)
            {
                faltam.Add($"referenciar {Exibir(chave.Referenciada.Nome)}");
            }

            if (faltam.Count > 0)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.SemRecriarChave,
                    $"A chave estrangeira {NomeTabela.Parte(chave.Chave.Nome)} do destino é removida e recriada na cópia, e o login não pode {Juntar(faltam)}, ou o dialeto não informa: copie com um login que possa alterá-las (no PostgreSQL, o dono das tabelas).",
                    chave.Filho.NomeTabela,
                    objeto: chave.Chave.Nome));
            }
        }
    }

    // As sequences do destino: a usada por coluna de tabela copiada é ajustada depois da carga; a outra sem ajuste só avisa.
    private void ConferirSequences(Permissoes permissoes)
    {
        var porNome = _destino.Esquema.Tabelas.ToDictionary(tabela => tabela.Nome);
        foreach (var sequence in _destino.Esquema.Sequences)
        {
            if (permissoes.Sequences.GetValueOrDefault(sequence.Nome) == true)
            {
                continue;
            }

            var copiadas = sequence.Colunas
                .Select(coluna => porNome.TryGetValue(coluna.Tabela, out var tabela) ? _unidadeDe.GetValueOrDefault(tabela) : null)
                .OfType<Unidade>()
                .Where(unidade => _acoes[unidade].Acao == AcaoTabela.Copiar)
                .OrderBy(unidade => unidade.NomeTabela, Comparer<NomeTabela>.Create(ColetorDoPlano.CompararTabelas))
                .ToList();
            var nome = Exibir(sequence.Nome);
            if (copiadas.Count > 0)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.SemAjustarSequence,
                    $"A sequence {nome} do destino é usada por tabela copiada, e o login não pode ajustá-la depois da carga, ou o dialeto não informa: o próximo valor repetiria um copiado. Dê a permissão na sequence.",
                    copiadas[0].NomeTabela,
                    objeto: sequence.Nome.Nome));
            }
            else
            {
                _coletor.Aviso(new Aviso(
                    CodigosPlanejar.SequenceSemAjuste,
                    $"A sequence {nome} do destino não é usada por tabela copiada, e o login não pode ajustá-la, ou o dialeto não informa: ela fica como o destino a tem.",
                    objeto: sequence.Nome.Nome));
            }
        }
    }

    // Os objetos residuais do destino: num destino restaurado, guardam dado real; a materialized view sem dados fica de fora.
    private void ConferirResiduais(Permissoes? permissoes)
    {
        foreach (var residual in _destino.Esquema.ObjetosResiduais)
        {
            var visao = residual.Tipo == TipoObjetoResidual.VisaoMaterializada;
            if (visao && residual.Populada == false)
            {
                continue;
            }

            var objeto = residual.Tipo switch
            {
                TipoObjetoResidual.VisaoMaterializada => $"a materialized view {Exibir(residual.Nome)}",
                TipoObjetoResidual.VisaoIndexada => $"a view indexada {Exibir(residual.Nome)}",
                TipoObjetoResidual.CapturaDeMudancas => $"a captura de mudanças {Exibir(residual.Nome)}",
                _ => "os large objects do banco",
            };
            if (permissoes is not null && permissoes.Residuais.GetValueOrDefault((residual.Nome, residual.Tipo)) != true)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.ResidualSemLimpeza,
                    $"O login do destino não pode limpar {objeto}, ou o dialeto não informa: num destino restaurado, o objeto guarda dado real, que sobraria. Dê a permissão, ou remova o objeto antes de copiar.",
                    objeto: residual.Nome.Nome));
            }

            if (visao && residual.DependenciasIncompletas != false)
            {
                _coletor.Aviso(new Aviso(
                    CodigosPlanejar.DependenciasIncompletas,
                    $"O dialeto não sabe dizer tudo o que {objeto} lê (uma função, um operador de usuário ou um ciclo): a atualização se repete até nada mudar.",
                    objeto: residual.Nome.Nome));
            }
        }
    }

    // A coluna de tipo que o Incognito não suporta, copiada como veio ou numa tabela ignorada do destino (no PostgreSQL, a
    // que aponta large objects): se ela aponta para fora da tabela, o que aponta é limpo no destino. O tipo vem da situação,
    // nunca do nome; a estrangeira, em que toda coluna é não suportada, nunca é tocada.
    private void AvisarNaoSuportadas()
    {
        const string Mensagem = "A coluna é de um tipo que o Incognito não suporta, e o valor fica como veio: se ela aponta para objetos fora da tabela (no PostgreSQL, os large objects), eles são limpos no destino, e a referência fica sem par.";
        foreach (var (unidade, pares) in _pares)
        {
            foreach (var par in pares.Where(par => (par.Origem.Situacao == SituacaoTipo.NaoSuportado || par.Destino.Situacao == SituacaoTipo.NaoSuportado) && !_efetivas.ContainsKey(new No(unidade, par.Origem.Nome))))
            {
                _coletor.Aviso(new Aviso(CodigosPlanejar.NaoSuportadaCopiada, Mensagem, unidade.NomeTabela, par.Origem.Nome), par.Indice);
            }
        }

        foreach (var unidade in _unidades.Where(unidade => _acoes[unidade].Acao == AcaoTabela.Ignorar && unidade.Destino is { Estrangeira: false }))
        {
            var colunas = unidade.Destino!.Colunas;
            for (var indice = 0; indice < colunas.Count; indice++)
            {
                if (colunas[indice].Situacao == SituacaoTipo.NaoSuportado)
                {
                    _coletor.Aviso(new Aviso(CodigosPlanejar.NaoSuportadaCopiada, Mensagem, unidade.NomeTabela, colunas[indice].Nome), indice);
                }
            }
        }
    }

    // A codificação, a versão e o transporte dos dois lados.
    private void ConferirBanco()
    {
        if (_destino.Esquema.TriggersDeEvento is not { Count: 0 })
        {
            var quais = _destino.Esquema.TriggersDeEvento is { } nomes ? $"há ({string.Join(", ", nomes.Select(TextoSeguro.Nome))})" : "o dialeto não informa se há";
            _coletor.Aviso(new Aviso(
                CodigosPlanejar.TriggersDeEvento,
                $"No destino, {quais} gatilhos de evento ligados: eles disparam quando a cópia remove e recria as chaves estrangeiras e desliga as triggers, e podem barrá-la; só quem administra o servidor os desliga."));
        }

        var daOrigem = LerPropriedades(_origem.Esquema, "na origem");
        var doDestino = LerPropriedades(_destino.Esquema, "no destino");
        if (daOrigem is not null && doDestino is not null)
        {
            var (origem, destino) = (daOrigem.Codificacao, doDestino.Codificacao);
            var (nomeDaOrigem, nomeDoDestino) = (TextoSeguro.Nome(origem.Nome), TextoSeguro.Nome(destino.Nome));
            if (!origem.Definida && destino.Definida)
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.OrigemSemCodificacao,
                    $"A origem guarda bytes sem codificação declarada ({nomeDaOrigem}), e o destino tem uma ({nomeDoDestino}): um byte que não forma caractere nela faz a cópia falhar, com o valor na mensagem do banco. Copie para um destino sem codificação declarada, ou converta a origem."));
            }
            else if (origem.Definida && !destino.Definida)
            {
                _coletor.Aviso(new Aviso(
                    CodigosPlanejar.DestinoSemCodificacao,
                    $"O destino guarda bytes sem codificação declarada ({nomeDoDestino}), e a origem tem uma ({nomeDaOrigem}): os limites de comprimento do destino passam a contar bytes."));
            }
            else if (origem.Definida && destino.Definida && !destino.Unicode && (origem.Unicode || !string.Equals(origem.Nome, destino.Nome, StringComparison.Ordinal)))
            {
                _coletor.Erro(new ErroPlano(
                    CodigosPlanejar.CodificacaoSemRepertorio,
                    $"A codificação da origem ({nomeDaOrigem}) tem caracteres que a do destino ({nomeDoDestino}) não representa: um texto copiado como veio faz a cópia falhar, com o valor na mensagem do banco. Crie o destino na codificação da origem, ou em UTF-8."));
            }

            // Só a principal, e só entre lados do mesmo dialeto, que a configuração já exige.
            if (daOrigem.Versao.Major != doDestino.Versao.Major)
            {
                _coletor.Aviso(new Aviso(
                    CodigosPlanejar.VersaoDiferente,
                    $"A versão principal do servidor difere entre a origem ({daOrigem.Versao.Major.ToString(CultureInfo.InvariantCulture)}) e o destino ({doDestino.Versao.Major.ToString(CultureInfo.InvariantCulture)})."));
            }
        }

        AvisarTransporte(_origem.Identidade.Transporte, "com a origem");
        AvisarTransporte(_destino.Identidade.Transporte, "com o destino");

        // O marcador, o banco esperado e a linhagem: o destino ainda sem marcador só avisa aqui.
        ConferenciaDoDestino.Conferir(_origem.Identidade, _destino.Identidade, _configuracao.Destino.BancoEsperado, exigirMarcador: false, _coletor);
    }

    private PropriedadesBanco? LerPropriedades(EsquemaBanco esquema, string lado)
    {
        if (esquema.Propriedades is null)
        {
            _coletor.Erro(new ErroPlano(
                CodigosPlanejar.PropriedadesNaoInformadas,
                $"O dialeto não informa a codificação e a versão do banco {lado}: sem elas, a conversão dos textos não pode ser conferida."));
        }

        return esquema.Propriedades;
    }

    // Fora da própria máquina, só a conexão cifrada com o servidor verificado não avisa.
    private void AvisarTransporte(TransporteConexao? transporte, string lado)
    {
        if (transporte is not { Local: true } && (transporte?.Cifrado != true || transporte.ServidorVerificado != true))
        {
            _coletor.Aviso(new Aviso(
                CodigosPlanejar.TransporteEmClaro,
                $"A conexão {lado} não é cifrada com o servidor verificado, ou o dialeto não informa: os dados da cópia podem passar em claro pela rede, ou por um intermediário."));
        }
    }

    // O que falta, numa frase: "a, b nem c".
    private static string Juntar(List<string> itens) =>
        itens.Count == 1 ? itens[0] : string.Join(", ", itens.Take(itens.Count - 1)) + " nem " + itens[^1];

    /// <summary>As permissões de um lado, por objeto.</summary>
    private sealed record Permissoes(
        Dictionary<NomeObjeto, PermissoesTabela> Tabelas,
        Dictionary<(NomeObjeto Nome, TipoObjetoResidual Tipo), bool?> Residuais,
        Dictionary<NomeObjeto, bool?> Sequences);

    /// <summary>Uma chave estrangeira do destino que o diário remove e recria.</summary>
    private sealed record ChaveDoDiario(TabelaEsquema Tabela, ChaveEstrangeira Chave, TabelaEsquema Referenciada, Unidade Filho);
}
