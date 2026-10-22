// As restrições lidas do destino são tipos do contrato experimental de dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// As restrições do destino que o pseudônimo pode violar (P8, com a unicidade): a restrição do tipo da coluna tratada, o <c>CHECK</c> imposto
/// e a chave de partição que citam uma tratada. São avisos: o dado decide.
/// </summary>
internal sealed partial class MontagemDoPlano
{
    // Os avisos das restrições de cada unidade copiada com coluna tratada.
    private void AvisarRestricoes()
    {
        foreach (var unidade in _unidades.Where(_pares.ContainsKey))
        {
            var tratadas = TratadasNoDestino(unidade);
            if (tratadas.Count == 0)
            {
                continue;
            }

            var nome = unidade.NomeTabela;

            // A tratada e a gerada calculada dela: a restrição do tipo vale no valor que o destino grava ou recalcula.
            foreach (var coluna in unidade.Destino!.Colunas.Where(coluna => tratadas.Contains(coluna.Nome) && coluna.RestritaPeloTipo != false))
            {
                var par = _pares[unidade].FirstOrDefault(par => par.Destino.Nome == coluna.Nome);
                _coletor.Aviso(
                    new Aviso(
                        CodigosPlanejar.RestritaPeloTipo,
                        "O tipo da coluna no destino tem uma restrição própria (o CHECK de um domínio, uma regra ligada ao tipo), ou o dialeto não informa: o pseudônimo pode violá-la.",
                        nome,
                        par?.Origem.Nome ?? coluna.Nome),
                    par?.Indice);
            }

            foreach (var tabela in TabelasNoDestino(unidade))
            {
                foreach (var check in tabela.RestricoesCheck.Where(check => check.Imposta && Cita(check.Referencias, tratadas)))
                {
                    _coletor.Aviso(new Aviso(
                        CodigosPlanejar.CheckComTratada,
                        $"O CHECK {NomeTabela.Parte(check.Nome)} do destino cita coluna tratada: o pseudônimo pode violá-lo, e a gravação da linha falharia.",
                        nome,
                        objeto: check.Nome));
                }

                if (tabela.ChaveParticao is { } particao && (particao.ColunasIncompletas || particao.Colunas.Any(tratadas.Contains)))
                {
                    _coletor.Aviso(new Aviso(
                        CodigosPlanejar.ParticaoComTratada,
                        "A chave de partição do destino cita coluna tratada, ou o dialeto não informa todas as colunas dela: o pseudônimo pode mudar a partição da linha, ou não achar nenhuma.",
                        nome));
                }
            }
        }
    }

    // As colunas tratadas da unidade, pelo nome no destino, mais as geradas calculadas a partir delas (a gerada sem as
    // referências, ou com elas incompletas, conta como calculada a partir de todas), até nada mudar.
    private HashSet<string> TratadasNoDestino(Unidade unidade)
    {
        var tratadas = _pares[unidade].Where(par => _efetivas.ContainsKey(new No(unidade, par.Origem.Nome))).Select(par => par.Destino.Nome).ToHashSet(StringComparer.Ordinal);
        if (tratadas.Count == 0)
        {
            return tratadas;
        }

        var geradas = TabelasNoDestino(unidade).SelectMany(tabela => tabela.Colunas).Where(coluna => coluna.Geracao != GeracaoColuna.Nenhuma).ToList();
        for (var mudou = true; mudou;)
        {
            mudou = false;
            foreach (var gerada in geradas.Where(gerada => !tratadas.Contains(gerada.Nome)))
            {
                if (gerada.ReferenciasDaGeracao is not { } referencias || Cita(referencias, tratadas))
                {
                    mudou |= tratadas.Add(gerada.Nome);
                }
            }
        }

        return tratadas;
    }

    // A tabela do destino da unidade e as partições dela.
    private IEnumerable<TabelaEsquema> TabelasNoDestino(Unidade unidade) =>
        _destino.Esquema.Tabelas.Where(tabela => ReferenceEquals(_unidadeDe.GetValueOrDefault(tabela), unidade));

    // Se a expressão cita alguma coluna do conjunto; com as referências incompletas, cita todas.
    private static bool Cita(ReferenciasDaExpressao referencias, HashSet<string> colunas) =>
        referencias.Incompletas || referencias.Colunas.Any(colunas.Contains);
}
