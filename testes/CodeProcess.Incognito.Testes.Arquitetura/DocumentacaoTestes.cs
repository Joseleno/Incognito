using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeProcess.Incognito.Testes.Arquitetura;

/// <summary>
/// A documentação versionada (todo <c>.md</c> do git) não traz documento válido nem promessa jurídica: identificadores só
/// gerados na hora, e nenhum texto diz que o uso da ferramenta cumpre a LGPD.
/// </summary>
public sealed class DocumentacaoTestes
{
    // As afirmações que a documentação nunca faz, comparadas sem caixa e sem acento. As negações ("não garante
    // anonimização nem conformidade", "não é anonimização garantida") são o texto certo e passam.
    private static readonly string[] _promessasProibidas =
    [
        "atende a lgpd",
        "atende a lei geral de protecao de dados",
        "garante conformidade",
        "garante a conformidade",
        "em conformidade com a lgpd",
        "anonimizacao certificada",
        "garante total conformidade",
        "assegura a conformidade",
        "atende plenamente",
        "lgpd compliant",
        "compliance com a lgpd",
    ];

    public static TheoryData<string> Documentos()
    {
        var documentos = new TheoryData<string>();
        foreach (var arquivo in Repositorio.Versionados("*.md"))
        {
            documentos.Add(arquivo);
        }

        return documentos;
    }

    [Fact]
    public void A_varredura_ve_a_documentacao()
    {
        Assert.Contains("README.md", Repositorio.Versionados("*.md"));
        Assert.Contains("docs/adr/0014-entrada-invalida-preservando-a-invalidade.md", Repositorio.Versionados("*.md"));
    }

    [Theory]
    [MemberData(nameof(Documentos))]
    public void Documento_nao_traz_CPF_CNPJ_nem_chave_de_acesso_validos(string arquivo)
    {
        // Forma canônica (dígitos de largura total viram ASCII) e sem a marcação que o Markdown deixa no meio de um número.
        var texto = Regex.Replace(File.ReadAllText(Path.Combine(Repositorio.Raiz(), arquivo)).Normalize(NormalizationForm.FormKC), @"[\\*_`]", "");
        var achados = new List<string>();

        // Dígitos: os separadores entre dois dígitos saem (máscara, espaço, quebra de linha, travessão), e a janela desliza
        // pela sequência inteira atrás de CPF e de chave de acesso.
        var digitos = Regex.Replace(texto, @"(?<=[0-9])[\s.\-/\p{Pd}]+(?=[0-9])", "");
        foreach (Match sequencia in Regex.Matches(digitos, "[0-9]{11,}"))
        {
            var valor = sequencia.Value;
            for (var inicio = 0; inicio + 11 <= valor.Length; inicio++)
            {
                if (ValidadorBr.Cpf(valor.AsSpan(inicio, 11)))
                {
                    achados.Add($"CPF perto da posição {sequencia.Index + inicio}");
                }
            }

            for (var inicio = 0; inicio + 44 <= valor.Length; inicio++)
            {
                if (ValidadorBr.ChaveDfe(valor.AsSpan(inicio, 44)))
                {
                    achados.Add($"chave de acesso perto da posição {sequencia.Index + inicio}");
                }
            }
        }

        // CNPJ alfanumérico: só palavra inteira, com ou sem a máscara. Uma janela deslizando dentro de palavras longas
        // (resumos SHA, identificadores) acharia DV certo por acaso.
        foreach (Match candidato in Regex.Matches(texto.ToUpperInvariant(), @"(?<![0-9A-Z])(?:[0-9A-Z]{14}|[0-9A-Z]{2}\.[0-9A-Z]{3}\.[0-9A-Z]{3}/[0-9A-Z]{4}-[0-9]{2})(?![0-9A-Z])"))
        {
            if (ValidadorBr.Cnpj(candidato.Value))
            {
                achados.Add($"CNPJ na posição {candidato.Index}");
            }
        }

        // Só a posição, nunca o valor: a mensagem do teste também é saída.
        Assert.True(achados.Count == 0, $"{arquivo}: {string.Join("; ", achados.Distinct())}.");
    }

    [Theory]
    [MemberData(nameof(Documentos))]
    public void Documento_nao_promete_conformidade(string arquivo)
    {
        // Sem ICU, a decomposição não separa os acentos e o teste ficaria cego.
        Assert.Equal(2, "ã".Normalize(NormalizationForm.FormD).Length);
        var semMarcacao = Regex.Replace(File.ReadAllText(Path.Combine(Repositorio.Raiz(), arquivo)).Normalize(NormalizationForm.FormKC), @"[*_`\u00AD]|&nbsp;", "");
        var texto = Regex.Replace(SemAcento(semMarcacao).ToLowerInvariant(), @"[\s\-]+", " ");

        Assert.All(_promessasProibidas, promessa => Assert.DoesNotContain(promessa, texto, StringComparison.Ordinal));
    }

    private static string SemAcento(string texto)
    {
        var construtor = new StringBuilder(texto.Length);
        foreach (var caractere in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                construtor.Append(caractere);
            }
        }

        return construtor.ToString();
    }
}
