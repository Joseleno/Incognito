namespace CodeProcess.Incognito.Testes;

/// <summary>
/// Apoio dos testes estatísticos: o p-valor do qui-quadrado pela função gama incompleta regularizada (conferida contra
/// quantis tabelados nos testes), o escore z e a correlação de postos de Spearman. Os testes usam semente fixa e são de
/// fumaça contra defeitos grosseiros; não provam segurança.
/// </summary>
internal static class Estatistica
{
    /// <summary>
    /// Limite do escore z (bicaudal, p ≈ 5,7·10⁻⁷ por conferência pela normal). São cerca de 470 conferências, e nas
    /// contagens de média pequena (pontos fixos, 2-ciclos, avalanche em base 36) a cauda exata chega a cerca de sete vezes
    /// a normal: a chance de uma permutação boa falhar por acaso numa troca de semente fica perto de 5·10⁻⁴.
    /// </summary>
    internal const double LimiteZ = 5.0;

    /// <summary>Alfa de cada qui-quadrado: o p bicaudal de z = 5, o mesmo do <see cref="LimiteZ"/>.</summary>
    internal const double AlfaQuiQuadrado = 5.7e-7;

    /// <summary>O p-valor (cauda superior) de uma estatística qui-quadrado com <paramref name="graus"/> graus de liberdade.</summary>
    internal static double PValorQuiQuadrado(double estatistica, int graus) => GamaQ(graus / 2.0, estatistica / 2.0);

    /// <summary>A estatística qui-quadrado de Pearson.</summary>
    internal static double QuiQuadrado(ReadOnlySpan<long> observados, ReadOnlySpan<double> esperados)
    {
        if (observados.Length != esperados.Length || observados.IsEmpty)
        {
            throw new ArgumentException("Observados e esperados precisam ter o mesmo comprimento, maior que zero.", nameof(esperados));
        }

        var soma = 0.0;
        for (var i = 0; i < observados.Length; i++)
        {
            if (!(esperados[i] > 0))
            {
                throw new ArgumentException("Esperado zero ou negativo no qui-quadrado.", nameof(esperados));
            }

            var diferenca = observados[i] - esperados[i];
            soma += diferenca * diferenca / esperados[i];
        }

        return soma;
    }

    /// <summary>O escore z de uma contagem com a média e a variância esperadas.</summary>
    internal static double Z(double observado, double media, double variancia) => (observado - media) / Math.Sqrt(variancia);

    /// <summary>
    /// A correlação de postos de Spearman entre duas sequências sem empates relevantes; sob independência, ρ·√(n − 1) é
    /// aproximadamente normal padrão.
    /// </summary>
    internal static double Spearman(IReadOnlyList<ulong> x, IReadOnlyList<ulong> y)
    {
        if (x.Count != y.Count || x.Count < 3)
        {
            throw new ArgumentException("As sequências precisam ter o mesmo comprimento, de pelo menos 3.", nameof(y));
        }

        var postosX = Postos(x);
        var postosY = Postos(y);
        double n = x.Count;
        var somaDosQuadrados = 0.0;
        for (var i = 0; i < x.Count; i++)
        {
            var diferenca = postosX[i] - postosY[i];
            somaDosQuadrados += diferenca * diferenca;
        }

        return 1 - (6 * somaDosQuadrados / (n * ((n * n) - 1)));
    }

    /// <summary>Q(s, x) = Γ(s, x) / Γ(s): série para x &lt; s + 1, fração contínua de Lentz no resto.</summary>
    internal static double GamaQ(double s, double x)
    {
        if (!(s > 0) || double.IsNaN(x))
        {
            throw new ArgumentOutOfRangeException(nameof(s), "A gama incompleta exige s > 0 e x definido.");
        }

        if (x <= 0)
        {
            return 1;
        }

        var logPrefixo = (s * Math.Log(x)) - x - LogGama(s);
        if (x < s + 1)
        {
            // P(s, x) pela série; Q = 1 − P.
            var termo = 1 / s;
            var soma = termo;
            for (var n = 1; Math.Abs(termo) > Math.Abs(soma) * 1e-15; n++)
            {
                if (n == 1_000)
                {
                    throw new InvalidOperationException("A série da gama incompleta não convergiu.");
                }

                termo *= x / (s + n);
                soma += termo;
            }

            return 1 - (soma * Math.Exp(logPrefixo));
        }

        const double Minimo = 1e-300;
        var b = x + 1 - s;
        var c = 1 / Minimo;
        var d = 1 / b;
        var h = d;
        for (var i = 1; ; i++)
        {
            if (i == 1_000)
            {
                throw new InvalidOperationException("A fração contínua da gama incompleta não convergiu.");
            }

            var an = -i * (i - s);
            b += 2;
            d = (an * d) + b;
            d = Math.Abs(d) < Minimo ? Minimo : d;
            c = b + (an / c);
            c = Math.Abs(c) < Minimo ? Minimo : c;
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < 1e-15)
            {
                break;
            }
        }

        return Math.Exp(logPrefixo) * h;
    }

    // ln Γ(s) pela aproximação de Lanczos (g = 7, 9 coeficientes): erro absoluto da ordem de 10⁻¹⁴ até s = 20 e de 10⁻¹³
    // até s = 100.
    private static double LogGama(double s)
    {
        ReadOnlySpan<double> coeficientes =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        ];
        s -= 1;
        var soma = coeficientes[0];
        for (var i = 1; i < coeficientes.Length; i++)
        {
            soma += coeficientes[i] / (s + i);
        }

        var t = s + 7.5;
        return (0.5 * Math.Log(2 * Math.PI)) + ((s + 0.5) * Math.Log(t)) - t + Math.Log(soma);
    }

    private static double[] Postos(IReadOnlyList<ulong> valores)
    {
        var indices = Enumerable.Range(0, valores.Count).ToArray();
        Array.Sort(indices, (i, j) => valores[i].CompareTo(valores[j]));
        var postos = new double[valores.Count];
        for (var posto = 0; posto < indices.Length; posto++)
        {
            postos[indices[posto]] = posto;
        }

        return postos;
    }
}
