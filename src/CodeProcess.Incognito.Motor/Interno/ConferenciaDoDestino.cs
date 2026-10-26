// A conferência lê a identidade, tipo do contrato experimental de dialeto que o motor declara; nada daqui é público.
#pragma warning disable INCX001

using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// O que a identidade dos dois lados diz da proteção do destino: o marcador do Incognito em cada lado, o
/// <c>bancoEsperado</c> e a linhagem. O <c>planejar</c> avisa o destino ainda sem marcador; a proteção do destino, antes de
/// escrever, o recusa. Nenhuma mensagem traz o que foi lido, fora o nome do banco no <c>INC3603</c>, escapado.
/// </summary>
internal static class ConferenciaDoDestino
{
    /// <summary>Confere as duas identidades e junta os erros e os avisos.</summary>
    /// <param name="origem">A identidade da origem.</param>
    /// <param name="destino">A identidade do destino.</param>
    /// <param name="bancoEsperado">O <c>destino.bancoEsperado</c> da configuração; nulo quando não escrito.</param>
    /// <param name="exigirMarcador">
    /// Se o destino sem marcador é recusado (<c>INC3600</c>, antes de escrever) ou só avisado (<c>INC3583</c>, no
    /// <c>planejar</c>).
    /// </param>
    /// <param name="coletor">Onde juntar.</param>
    internal static void Conferir(IdentidadeLida origem, IdentidadeLida destino, string? bancoEsperado, bool exigirMarcador, ColetorDoPlano coletor)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(coletor);

        // A origem: o marcador nulo é o pior caso, um destino preparado.
        switch (origem.Marcador?.Situacao)
        {
            case null:
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.OrigemMarcada,
                    "O dialeto não informa se a origem tem o marcador do Incognito: sem saber, ela pode ser um destino preparado, que nunca é origem."));
                break;
            case SituacaoMarcador.Valido:
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.OrigemMarcada,
                    "A origem tem o marcador do Incognito: um banco preparado como destino nunca é origem. Confira se as conexões da origem e do destino não estão trocadas."));
                break;
            case SituacaoMarcador.Invalido:
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.MarcadorIlegivel,
                    "A origem tem um marcador do Incognito que não se lê: ela pode ser um destino preparado, que nunca é origem. Confira as conexões, ou apague o comentário do banco."));
                break;
        }

        switch (destino.Marcador?.Situacao)
        {
            case SituacaoMarcador.Valido:
                break;
            case SituacaoMarcador.Invalido:
                coletor.Erro(new ErroPlano(
                    CodigosPlanejar.MarcadorIlegivel,
                    "O destino tem um marcador do Incognito que não se lê: apague o comentário do banco e rode o preparar-destino de novo."));
                break;
            case var situacao when exigirMarcador:
                coletor.Erro(new ErroPlano(CodigosPlanejar.DestinoSemMarcador, $"{SemMarcador(situacao)}: rode o preparar-destino antes de executar."));
                break;
            case var situacao:
                coletor.Aviso(new Aviso(CodigosPlanejar.DestinoAindaSemMarcador, $"{SemMarcador(situacao)}: rode o preparar-destino antes de executar, que o recusa sem ele."));
                break;
        }

        ConferirBanco(destino, bancoEsperado, coletor);
        AvisarLinhagem(origem, destino, coletor);
    }

    /// <summary>Confere o <c>bancoEsperado</c>: o nome conectado é o do catálogo, e a comparação é exata.</summary>
    /// <param name="destino">A identidade do destino.</param>
    /// <param name="bancoEsperado">O <c>destino.bancoEsperado</c>; nulo não confere.</param>
    /// <param name="coletor">Onde juntar.</param>
    internal static void ConferirBanco(IdentidadeLida destino, string? bancoEsperado, ColetorDoPlano coletor)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(coletor);
        if (bancoEsperado is not null && !string.Equals(bancoEsperado, destino.Banco, StringComparison.Ordinal))
        {
            coletor.Erro(new ErroPlano(
                CodigosPlanejar.BancoInesperado,
                $"O destino conectado é o banco {TextoSeguro.Nome(destino.Banco)}, e a configuração espera {TextoSeguro.Nome(bancoEsperado)} (destino.bancoEsperado): confira a conexão do destino."));
        }
    }

    /// <summary>
    /// Avisa a linhagem ou o endereço iguais entre os lados, dizendo só o que coincidiu, nunca os valores (<c>INC3582</c>).
    /// </summary>
    /// <param name="origem">A identidade da origem.</param>
    /// <param name="destino">A identidade do destino.</param>
    /// <param name="coletor">Onde juntar.</param>
    internal static void AvisarLinhagem(IdentidadeLida origem, IdentidadeLida destino, ColetorDoPlano coletor)
    {
        ArgumentNullException.ThrowIfNull(coletor);
        var linhagem = MesmaLinhagem(origem, destino);
        var endereco = MesmoEndereco(origem, destino);
        if (linhagem || endereco)
        {
            var oQue = (linhagem, endereco) switch
            {
                (true, true) => "a mesma linhagem e o mesmo endereço",
                (true, false) => "a mesma linhagem (o mesmo banco, ou uma cópia física, um snapshot ou uma recuperação dele)",
                _ => "o mesmo endereço",
            };
            coletor.Aviso(new Aviso(
                CodigosPlanejar.MesmaLinhagem,
                $"A origem e o destino têm {oQue}: confira que o destino é a cópia que você quer sobrescrever, e não a origem."));
        }
    }

    /// <summary>
    /// Se as linhagens coincidem: as duas informam as mesmas entradas, ao menos duas (uma só, como o identificador do banco
    /// dentro do servidor, coincide por acaso entre servidores), todas iguais.
    /// </summary>
    /// <param name="origem">A identidade da origem.</param>
    /// <param name="destino">A identidade do destino.</param>
    /// <returns>Se coincidem.</returns>
    internal static bool MesmaLinhagem(IdentidadeLida origem, IdentidadeLida destino)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        var (uma, outra) = (origem.Linhagem, destino.Linhagem);
        return uma.Count >= 2
            && uma.Count == outra.Count
            && uma.All(entrada => outra.TryGetValue(entrada.Key, out var valor) && string.Equals(valor, entrada.Value, StringComparison.Ordinal));
    }

    /// <summary>Se o endereço coincide; vazio (conexão local, sem endereço) não informa e nunca coincide.</summary>
    /// <param name="origem">A identidade da origem.</param>
    /// <param name="destino">A identidade do destino.</param>
    /// <returns>Se coincide.</returns>
    internal static bool MesmoEndereco(IdentidadeLida origem, IdentidadeLida destino)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);
        return origem.Servidor.Length > 0 && string.Equals(origem.Servidor, destino.Servidor, StringComparison.Ordinal);
    }

    private static string SemMarcador(SituacaoMarcador? situacao) => situacao switch
    {
        null => "O dialeto não informa se o destino tem o marcador do Incognito",
        SituacaoMarcador.Alheio => "O destino não tem o marcador do Incognito (o banco tem um comentário que não é dele)",
        _ => "O destino não tem o marcador do Incognito",
    };
}
