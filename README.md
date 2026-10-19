# CodeProcess.Incognito

[![ci](https://github.com/Joseleno/Incognito/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/Joseleno/Incognito/actions/workflows/ci.yml?query=branch%3Adevelop)
[![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/Joseleno/Incognito?label=openssf%20scorecard)](https://scorecard.dev/viewer/?uri=github.com/Joseleno/Incognito)

Times brasileiros copiam a base de produção para homologação, desenvolvimento e CI porque precisam de dados realistas, e esses ambientes costumam ser menos protegidos que a produção. O Incognito é uma biblioteca NuGet e uma ferramenta de linha de comando em .NET 10, em construção, para copiar bases PostgreSQL e SQL Server substituindo dados pessoais por dados brasileiros válidos: CPF, CNPJ alfanumérico, telefone, chaves de acesso de NF-e e demais DF-e.

Gratuita, open source (MIT) e inteiramente em português.

> **Em desenvolvimento.** Nenhum pacote foi publicado. A primeira publicação no NuGet será a `1.0.0`, com todos os pacotes de uma vez. Hoje só o núcleo (`CodeProcess.Incognito`) está completo: geração, validação e pseudonimização de documentos brasileiros, sem banco. O `Motor` já lê a configuração e o `PostgreSql` já lê o esquema de um banco, mas nenhum pacote copia banco ainda; `SqlServer`, `EntityFrameworkCore` e `Cli` são esqueletos, e o comando `incognito` ainda não faz nada.
>
> **Até a `1.0.0`, nenhuma saída é contrato.** Pseudônimos e sequências do `GeradorBr` com semente podem mudar entre versões candidatas. Não guarde resultados de uma versão candidata contando reproduzi-los depois.

## Uso direto, sem banco

```csharp
using CodeProcess.Incognito;

// Geração de dados de teste, com semente opcional para reprodutibilidade
string cpf = GeradorBr.Padrao.Cpf(formatado: true);
var gerador = new GeradorBr(semente: 42);
string cnpj = gerador.Cnpj(alfanumerico: true);

// Validação (sobrecargas com ReadOnlySpan<char> para evitar alocação)
bool valido = ValidadorBr.Cpf(cpf);
bool chaveValida = ValidadorBr.ChaveDfe(gerador.ChaveDfe(modelo: 55));

// Pseudonimização: mesma chave, ambiente e domínio, mesmo resultado; thread-safe. A chave vem de uma variável
// de ambiente ou de um arquivo, nunca do código, e precisa viver enquanto o pseudonimizador for usado.
using var chave = ChaveIncognito.Ler(Environment.GetEnvironmentVariable("INCOGNITO_CHAVE"));
using var pseudonimizador = new Pseudonimizador(chave, ambiente: "homologacao");
string pseudonimo = pseudonimizador.Cpf(cpf);                   // domínio padrão cpf@1; continua sendo dado pessoal
```

O que o exemplo mostra:

- O `GeradorBr` gera CPF, CNPJ (numérico ou alfanumérico) e chave de acesso de DF-e que passam na validação. Com semente, repete a mesma sequência (numa única thread); o `GeradorBr.Padrao` sorteia uma sequência nova a cada execução.
- O `ValidadorBr` confere a estrutura e os dígitos verificadores e nunca lança: texto nulo ou fora do formato devolve `false`.
- O `Pseudonimizador` troca CPF, CNPJ, telefone, e-mail, chave de acesso de DF-e e chave Pix por valores do mesmo tipo e na mesma forma. A mesma chave, o mesmo ambiente e o mesmo valor dão sempre o mesmo pseudônimo, então as referências entre tabelas e sistemas continuam batendo. DV errado continua errado. Valor fora da forma lança `IncognitoException` com o código `INC1003`, sem o valor na mensagem (não registre a entrada ao tratar a exceção). Voltam como vieram, sem aviso: texto vazio, CPF de base repdígito (com qualquer DV), CNPJ de raiz zero, telefone zerado, números de serviço (0800, 0300, 0303, 0500, 0900, 3003 e 4004) e a chave de 44 zeros; na chave de DF-e, partes como o número 0 e o código numérico proibido também ficam, e às vezes a chave inteira.
- Se a variável `INCOGNITO_CHAVE` não existir ou não estiver no formato `inc1_` seguido de 43 caracteres, `ChaveIncognito.Ler` lança `IncognitoException` com o código `INC1001`, sem mostrar o texto lido.

### Criar uma chave

Enquanto o comando `incognito gerar-chave` não existe, a chave sai de um programa à parte, rodado uma vez:

```csharp
using CodeProcess.Incognito;

// Gera uma chave nova e grava num arquivo novo, que no Linux e no macOS só o seu usuário lê. Guarde o arquivo fora
// do repositório e do ambiente de destino: quem tem a chave reverte todos os pseudônimos.
using var nova = ChaveIncognito.Gerar();
Span<char> texto = stackalloc char[48];
if (!nova.TentarExportar(texto, out int escritos))
{
    throw new InvalidOperationException("Espaço insuficiente para a chave.");
}

// Sem buffer intermediário, e os bytes zerados depois: a chave em claro só fica no arquivo.
var opcoes = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, BufferSize = 0 };
if (!OperatingSystem.IsWindows())
{
    opcoes.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
}

Span<byte> bytes = stackalloc byte[48];
int gravados = System.Text.Encoding.ASCII.GetBytes(texto[..escritos], bytes);
using (var arquivo = new FileStream("incognito.chave", opcoes))
{
    arquivo.Write(bytes[..gravados]);
}

System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
texto.Clear();
Console.WriteLine($"Chave gravada em incognito.chave. Impressão digital: {nova.ImpressaoDigital}");
```

No Windows, grave o arquivo numa pasta que só o seu usuário acessa. Para usar a chave no exemplo:

```
export INCOGNITO_CHAVE="$(cat incognito.chave)"           # bash
$env:INCOGNITO_CHAVE = Get-Content -Raw incognito.chave   # PowerShell
```

Use uma chave por ambiente. A impressão digital identifica a chave sem revelá-la e pode ser registrada.

### Experimentar antes da publicação

Sem pacote no NuGet, gere o pacote do núcleo a partir do código. O build exige o SDK do .NET 10 na versão exata do `global.json`; o projeto que consome o pacote, não.

```
git clone https://github.com/Joseleno/Incognito.git
cd Incognito
dotnet pack src/CodeProcess.Incognito -c Release -o ../feed-incognito
cd ..
dotnet new console -o teste-incognito
```

Como o pacote não existe no NuGet, um `nuget.config` no `teste-incognito` manda buscá-lo só no feed local:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="../feed-incognito" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="CodeProcess.Incognito*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

```
dotnet add teste-incognito package CodeProcess.Incognito --prerelease
```

O `dotnet pack` roda de dentro do clone, para valer o SDK do `global.json`. Rode primeiro o programa da seção "Criar uma chave" no `Program.cs` do `teste-incognito` (ele recusa sobrescrever um `incognito.chave` que já exista), exporte a variável e troque o `Program.cs` pelo exemplo de uso direto. A mensagem de erro da chave cita o comando `incognito gerar-chave`, que só chega com a ferramenta. Para imprimir os valores, acrescente um `Console.WriteLine`: são gerados na hora, mas podem coincidir com documentos de pessoas e empresas reais, então não os copie para issues, documentação ou exemplos.

As versões candidatas (`v1.0.0-rc.N`) também geram os pacotes como artefato do workflow [`release`](https://github.com/Joseleno/Incognito/actions/workflows/release.yml), com attestation de origem, conferível com `gh attestation verify <arquivo>.nupkg --repo Joseleno/Incognito`. Eles não vão ao NuGet e o artefato expira.

## LGPD

Executar o Incognito é tratamento de dados pessoais: a leitura da base de origem (inclusive a amostragem do comando `descobrir`) precisa de base legal e finalidade compatível. A ferramenta reduz a exposição em ambientes não produtivos e apoia os princípios de necessidade, segurança, prevenção e prestação de contas (art. 6º, III, VII, VIII e X), as medidas dos arts. 46 (inclusive §2º) e 49, e os controles ISO/IEC 27001:2022 A.8.11, A.8.31 e A.8.33. Não garante anonimização nem conformidade. Este projeto não constitui aconselhamento jurídico.

A documentação do `Pseudonimizador` usa o enquadramento do modo `reproduzivel`: pseudonimização no sentido do art. 13, §4º, conceito que a lei define no contexto de estudos em saúde pública e que é aplicado aqui por analogia. O resultado é dado pessoal. A chave é a informação adicional que permite reverter todos os valores e deve ser mantida separada, em ambiente controlado, nunca no ambiente de destino.

## O que não faz

- **Não é ferramenta de conformidade.** A conformidade com a LGPD é do controlador e do processo.
- **Não torna o dado anônimo.** O pseudônimo continua sendo dado pessoal, e quem tem a chave reverte todos os valores (os permutados, em microssegundos). A instância do `Pseudonimizador` é tão sensível quanto a chave: nunca a exponha como serviço, endpoint ou fila a quem recebe os dados pseudonimizados.
- **Não garante dado fictício.** Válido não quer dizer inexistente: CPF, CNPJ (inclusive de microempreendedor, que é pessoa natural), telefone e chave de acesso de DF-e gerados ou pseudonimizados podem pertencer a pessoas e empresas reais. Uma homologação que envia SMS ou WhatsApp, consulta bureaus de crédito ou os portais das SEFAZ atinge essas pessoas e empresas.
- **O `GeradorBr` não é criptográfico.** Serve para dados de teste, nunca para senhas, tokens ou chaves.
- **Não envia dados a nenhum serviço.** O núcleo não acessa a rede, nem para conferir as referências normativas. Na `1.0.0`, a única chamada de rede além dos bancos será a verificação de versão no NuGet, desligada por padrão.
- **Não faz mascaramento dinâmico em produção** e não é serviço hospedado.

Quando a cópia de bancos chegar (`1.0.0`), também não vai:

- criar o esquema no destino, que precisa existir antes (por migrations ou cópia só da estrutura);
- alterar o banco de origem ou as configurações dele;
- copiar entre bancos de tipos diferentes (só PostgreSQL para PostgreSQL e SQL Server para SQL Server);
- retomar uma execução interrompida: recomeça do zero, de forma segura;
- detectar nomes de pessoas em texto livre;
- tratar o que está fora do banco copiado: logs, filas, caches, índices de busca, arquivos e backups;
- carregar plugins de terceiros pela linha de comando (extensão só pela API).

## Referências normativas

`CatalogoNormativo` (namespace `CodeProcess.Incognito.Normas`) lista as referências normativas que cada versão do pacote considera, com a data da última revisão de cada uma, e `CatalogoNormativo.RevisaoMaisAntiga` dá a data da revisão normativa embutida no pacote. Citar uma referência não indica conformidade com ela.

## Contribuir

Requer o SDK do .NET 10 na versão fixada no `global.json`:

```
dotnet build -c Release
dotnet test
```

- [Como contribuir](https://github.com/Joseleno/Incognito/blob/HEAD/CONTRIBUTING.md)
- [Registros de decisão (ADR)](https://github.com/Joseleno/Incognito/blob/HEAD/docs/adr/README.md)
- [Mudanças](https://github.com/Joseleno/Incognito/blob/HEAD/CHANGELOG.md)
- [Vulnerabilidades: relato privado](https://github.com/Joseleno/Incognito/blob/HEAD/SECURITY.md)
- [Licença MIT](https://github.com/Joseleno/Incognito/blob/HEAD/LICENSE)
