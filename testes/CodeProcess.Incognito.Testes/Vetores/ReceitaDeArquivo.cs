namespace CodeProcess.Incognito.Testes.Vetores;

/// <summary>Um arquivo de vetores: transformador, versão, descrição e casos.</summary>
internal sealed record ReceitaDeArquivo(string Arquivo, string Transformador, int Versao, string Descricao, IReadOnlyList<ReceitaDeCaso> Casos);
