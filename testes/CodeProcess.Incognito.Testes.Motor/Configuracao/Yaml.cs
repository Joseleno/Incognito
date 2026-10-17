using CodeProcess.Incognito.Configuracao;
using Xunit.Sdk;

namespace CodeProcess.Incognito.Testes.Motor.Configuracao;

/// <summary>Arquivos de configuração dos testes e atalhos para ler e colher os erros.</summary>
internal static class Yaml
{
    /// <summary>O menor arquivo válido; as referências de segredo são nomes de variável fictícios.</summary>
    internal const string Minimo = """
        versao: 1
        origem:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_ORIGEM
        destino:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_DESTINO
          bancoEsperado: loja_homolog
        modo: reproduzivel
        chave: ambiente:INCOGNITO_CHAVE
        ambiente: homologacao

        """;

    /// <summary>O exemplo da seção de configuração da documentação, com a chave numa variável.</summary>
    internal const string Exemplo = """
        versao: 1

        origem:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_ORIGEM

        destino:
          provedor: postgresql
          conexao: ambiente:INCOGNITO_DESTINO
          bancoEsperado: loja_homolog

        modo: reproduzivel                  # ou efemero
        chave: ambiente:INCOGNITO_CHAVE     # obrigatória em reproduzivel; proibida em efemero
        ambiente: homologacao

        politica:
          semClassificacao: copiar
          suspeitaSemRegra: falhar
          entradaInvalida: preservar
          entradaMalformada: falhar
          consistencia: melhorEsforco
          paralelismo: 4

        dominios:
          cpf:   { transformador: br.cpf@1 }
          cnpj:  { transformador: br.cnpj@1 }
          email: { transformador: email@1 }

        tabelas:
          public.clientes:
            colunas:
              cpf: cpf
              nome: { transformador: br.nome-completo@1, derivarDe: cpf }
              email: email
              telefone: { transformador: br.telefone@1 }
              cep: { transformador: br.cep@1, derivarDe: cpf }
              nascimento: { transformador: data.generalizar@1, precisao: ano }
              observacoes: { transformador: texto.substituir@1 }
              laudo: { acao: manter, classe: sensivel, justificativa: "campo exigido pelo teste de integração do parceiro" }
              criado_em: manter

          public.pedidos:
            colunas:
              cpf_comprador: cpf

          public.auditoria: { acao: esvaziar }
          public.outbox_messages: { acao: esvaziar }

        """;

    /// <summary>
    /// Teto dos testes de tempo: folga para o coverlet e para o runner do CI. Os casos que eles barram (ciclos cúbicos,
    /// regex que volta atrás, bomba de aliases) levam minutos ou não terminam com as mesmas entradas.
    /// </summary>
    internal static readonly TimeSpan LimiteDeTempo = TimeSpan.FromSeconds(30);

    /// <summary>O mínimo com um trecho acrescentado ao fim.</summary>
    internal static string Com(string trecho) => Minimo + trecho;

    /// <summary>O mínimo com uma linha trocada por outra (ou removida, com <paramref name="nova"/> nulo).</summary>
    internal static string Trocando(string linha, string? nova)
    {
        var texto = Minimo.Replace(linha + "\n", nova is null ? "" : nova + "\n", StringComparison.Ordinal);
        Assert.NotEqual(Minimo, texto);
        return texto;
    }

    internal static ConfiguracaoIncognito Ler(string texto) => ConfiguracaoIncognito.LerYaml(texto);

    internal static ConfiguracaoInvalidaException Excecao(string texto)
    {
        try
        {
            ConfiguracaoIncognito.LerYaml(texto);
        }
        catch (ConfiguracaoInvalidaException excecao)
        {
            return excecao;
        }

        throw new XunitException("A configuração deveria ter sido recusada.");
    }

    internal static IReadOnlyList<ErroConfiguracao> Erros(string texto) => Excecao(texto).Erros;

    internal static ErroConfiguracao UnicoErro(string texto) => Assert.Single(Erros(texto));

    /// <summary>Uma chave do Incognito gerada agora, por extenso (nunca escrita no repositório).</summary>
    internal static string ChaveGerada()
    {
        using var chave = ChaveIncognito.Gerar();
        Span<char> texto = stackalloc char[64];
        Assert.True(chave.TentarExportar(texto, out var escritos));
        return new string(texto[..escritos]);
    }

    /// <summary>Um caminho absoluto no sistema atual, sem existir.</summary>
    internal static string CaminhoAbsoluto(string nome) =>
        OperatingSystem.IsWindows() ? $@"C:\segredos\{nome}" : $"/run/secrets/{nome}";
}
