using System.Globalization;
using CodeProcess.Incognito.Pseudonimizacao;

namespace CodeProcess.Incognito.Transformadores.Embutidos;

/// <summary>
/// <c>id.permutar@1</c>: permuta os identificadores inteiros positivos de uma chave primária em <c>[1, N)</c>, com o N do
/// <see cref="ContextoCriacao.TamanhoDominio"/>; sem parâmetros. O domínio é o declarado na coluna: o padrão (o nome da
/// fábrica) nunca é usado pelo motor.
/// </summary>
internal sealed class FabricaIdentificador() : FabricaEmbutida("id.permutar", "id.permutar", Injetividade.Garantida, TipoColuna.Inteiro)
{
    private const int DigitosMinimos = 4;
    private const int DigitosMaximos = 18;

    protected override Transformador CriarCore(ContextoCriacao contexto)
    {
        if (contexto.TamanhoDominio is not { } tamanho)
        {
            throw new RegraInvalidaException("O tamanho do domínio (N) não foi informado: o id.permutar precisa dele para permutar [1, N).");
        }

        if (!PotenciaAceita(tamanho))
        {
            throw new RegraInvalidaException("O tamanho do domínio (N) precisa ser uma potência de 10 de 10⁴ a 10¹⁸.");
        }

        ExigirFaixa(contexto, tamanho - 1, $"o pseudônimo do id.permutar vai até N − 1, com N = {tamanho.ToString(CultureInfo.InvariantCulture)}");

        // N fora da identidade: duas colunas do mesmo domínio com N diferentes dariam permutações diferentes em silêncio, e a
        // chave estrangeira deixaria de apontar para a linha certa. A primeira coluna fixa o N do domínio na sessão.
        var regra = contexto.Regra;
        var identificador = contexto.Sessao.Obter(Identidade(regra), (chave, ambiente) => new PseudonimizadorIdentificador(
            chave, regra.Dominio.Nome, regra.Dominio.Versao, ambiente, tamanho, regra.EntradaMalformada));
        if (identificador.Tamanho != tamanho)
        {
            throw new RegraInvalidaException("O domínio já foi criado nesta sessão com outro tamanho (N): todas as colunas do domínio usam o mesmo N.");
        }

        return new TransformadorDeDocumento(Nome, Injetividade, contexto.Tipo, null, identificador.Pseudonimizar);
    }

    // Uma potência de 10 de 10⁴ a 10¹⁸, a faixa que o PseudonimizadorIdentificador aceita.
    private static bool PotenciaAceita(long tamanho)
    {
        var potencia = 1L;
        for (var digitos = 1; digitos <= DigitosMaximos; digitos++)
        {
            potencia *= 10;
            if (potencia == tamanho)
            {
                return digitos >= DigitosMinimos;
            }
        }

        return false;
    }
}
