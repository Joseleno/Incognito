# ADR 0021 — Normalização de texto própria e congelada, sem ICU nem cultura

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (derivação das subchaves), [ADR 0004](0004-modos-efemero-e-reproduzivel.md) (modo `reproduzivel`), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (versão do transformador e vetores fixos), [ADR 0014](0014-entrada-invalida-preservando-a-invalidade.md) (entrada malformada)

## Contexto

No modo `reproduzivel` (ADR 0004), o mesmo valor, com a mesma chave, domínio, versão e ambiente, dá o mesmo pseudônimo
em qualquer execução, e dentro de uma versão do transformador a saída nunca muda (ADR 0013). "Qualquer execução" quer
dizer também qualquer máquina: quem desenvolve no Windows, o CI no Linux, um contêiner com globalização invariante, a
mesma cópia refeita meses depois com outro patch do runtime ou outra biblioteca de Unicode no sistema.

Vários pontos tratam texto antes de calcular. O e-mail compara endereços sem distinguir maiúsculas e sem os espaços
das pontas; a chave Pix aceita CNPJ e UUID em minúsculas; a chave de DF-e aceita o prefixo do atributo `Id` em
qualquer caixa; a derivação recebe nomes de domínio, transformador e ambiente escritos por quem configura. Cada etapa
alimenta um HMAC ou uma permutação: um caractere tratado de outro jeito muda o pseudônimo inteiro, sem erro, e as
junções entre cópias deixam de bater em silêncio. Um teste de propriedade não vê isso, porque a saída continua com a
forma certa.

As ferramentas prontas do .NET não são estáveis nesse sentido:

- `ToLowerInvariant` e `ToUpperInvariant` dependem do modo de globalização do processo (ICU, na versão que o sistema
  traz; NLS no Windows, quando ligado; ou globalização invariante) e da versão do runtime. "Invariant" quer dizer
  independente da cultura, não do ambiente: caracteres como os do ramo `fora-da-regra` dos vetores de e-mail dão
  resultados diferentes entre esses modos.
- `string.Normalize` depende da biblioteca de globalização e pode não decompor os acentos sem ela. Os próprios
  testes da documentação conferem a decomposição antes de usá-la, para não ficarem cegos sem o ICU.
- Comparações com cultura dependem da cultura da máquina (o caso clássico é o `I` do turco).

E normalizar mais não é de graça: cada regra a mais (formas Unicode, caracteres confundíveis, regras de provedor de
e-mail) junta grafias que alguém considera diferentes e acrescenta uma tabela externa que pode mudar.

## Decisão

1. **Sem cultura e sem ICU no caminho do pseudônimo.** Nenhum transformador, nem a derivação, usa `ToLower` ou
   `ToUpper` (inclusive as variantes `Invariant`), `string.Normalize`, `CompareInfo`, `TextInfo` ou comparação com
   cultura. Comparações de texto são ordinais, e números escritos em texto (o `dominio@versao` da derivação, o N do
   `id.permutar` no ajuste) usam `CultureInfo.InvariantCulture`.
2. **Regra própria, fechada e congelada.** Toda normalização é uma regra escrita no código, sobre um conjunto fechado
   de caracteres, e faz parte da versão do transformador: mudar a regra muda a saída e exige versão nova (ADR 0013). A
   regra é escrita também na referência em Python dos vetores, a partir da descrição, e não da tabela de uma
   biblioteca.
3. **Nomes da derivação: recusar, não normalizar.** Domínio, transformador e ambiente seguem a regra
   `[a-z0-9]+([._-][a-z0-9]+)*`, com até 64 caracteres, conferida no próprio núcleo, para valer também para quem usa a
   biblioteca sem o motor. Maiúscula, acento em qualquer forma Unicode, espaço, BOM, caractere de largura zero, letra
   de largura total e surrogate isolado dão `ArgumentException`, sem o nome na mensagem, em vez de outra subchave. A
   variante é um conjunto fechado.
4. **E-mail, versão 1.** Saem os espaços das pontas. As minúsculas seguem regra própria: só `A`–`Z` e `À`–`Þ`, menos
   `×`, somam 32 ao código do caractere, o que cobre o português; qualquer outro caractere fica como veio (por exemplo
   `Σ`, `Ÿ`, `İ`, `ß` e o sinal de Kelvin). Não há normalização Unicode: `é` composto e `e` seguido do acento
   combinante são textos diferentes. Não há regra de provedor: pontos e `+` da parte local ficam. Surrogate sem par é
   entrada malformada, nunca trocado por U+FFFD. O texto vai ao HMAC em UTF-8.
5. **Documentos: só ASCII, e caixa só em ASCII.** Dígitos são só `0`–`9`; as letras do CNPJ alfanumérico, só `A`–`Z`
   maiúsculas. Na chave Pix, o CNPJ em minúsculas passa a maiúsculas só nas letras ASCII, e o UUID da chave aleatória
   é lido como bytes hexadecimais ASCII e escrito em minúsculas. O prefixo do `Id` da chave de DF-e é comparado sem
   caixa só em ASCII (`Ascii.EqualsIgnoreCase`). Dígito de outro alfabeto ou de largura total, `ı` sem ponto e o
   sinal de Kelvin nunca são convertidos: a entrada é malformada (ADR 0014).
6. **Entidade do CEP.** Saem só os espaços das pontas, e cada surrogate sem par vira U+FFFD antes do UTF-8 (ao
   contrário do e-mail, que o recusa); o núcleo não faz nenhuma outra normalização, e a mesma entidade precisa chegar
   escrita do mesmo jeito para dar o mesmo CEP.
7. **Resíduo aceito: espaço e controle.** O único uso de tabela Unicode que resta é saber o que é espaço e o que é
   controle: o corte das pontas (`Trim`, no e-mail e na entidade do CEP) e `char.IsWhiteSpace` e `char.IsControl` na
   forma do e-mail. Essas tabelas vêm no próprio runtime do .NET, não do ICU nem do sistema. Todos os espaços que o
   runtime reconhece hoje estão nos vetores, e os dois conjuntos estão escritos como listas de códigos na referência em
   Python.
8. **Transformadores novos que produzem texto** seguem os itens 1 e 2: a caixa da entrada é lida e aplicada por regra
   própria sobre um conjunto fechado, e as listas que eles usam vêm embutidas no pacote, nunca de fonte externa em
   tempo de execução. O primeiro previsto é o de nome completo (`br.nome-completo`), ainda não implementado: nome e
   dois sobrenomes, na caixa da entrada, com acentos, sem inferir sexo a partir do nome de entrada. A regra de caixa
   dele entra nos vetores da versão 1 desse transformador.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| `ToLowerInvariant` e `ToUpperInvariant` | O resultado muda entre ICU, NLS e globalização invariante e entre versões; quebraria o modo `reproduzivel` e os vetores sem nenhum erro |
| Exigir globalização invariante de quem usa a biblioteca | É escolha do aplicativo, não da biblioteca; e a tabela de caixa do modo invariante é a do runtime, que acompanha as versões do Unicode que ele adota |
| Normalização Unicode (NFC ou NFKC) antes do HMAC | Depende do ICU (sem ele, não decompõe); NFKC junta caracteres que a pessoa considera diferentes; a referência em Python precisaria da mesma versão do Unicode |
| Normalizar os nomes da derivação (minúsculas, NFC, sem espaços) | Grafias que parecem iguais ("produção" em NFC e em NFD, com BOM ou com espaço de largura zero) dariam subchaves diferentes se uma escapasse da normalização; recusar é simples, não depende de Unicode e protege também quem usa a biblioteca sem o motor |
| Regras de provedor no e-mail (pontos e `+`) | Variam por provedor e não vêm de norma; juntariam endereços que são diferentes em outros provedores |
| Transcrever a tabela de caixa completa do Unicode | Congelaria a regra, mas com centenas de entradas sem uso nos dados brasileiros, a conferir e transcrever também na referência; o português cabe em `A`–`Z` e `À`–`Þ` |
| Não tratar a caixa do e-mail | `Fulano@x` e `fulano@x` são, na prática, o mesmo endereço; pseudônimos diferentes quebrariam junções entre tabelas que gravam o e-mail com caixas diferentes |

## Consequências

- O pseudônimo não depende da máquina, do sistema nem do modo de globalização, por construção: nenhuma API que dependa
  deles é chamada. Os vetores conferidos em Linux, Windows e macOS confirmam nos três sistemas; nenhum job do CI roda
  com globalização invariante.
- Grafias fora da regra continuam diferentes: `Σ` e `σ`, `é` composto e decomposto, letras de outros alfabetos em
  caixas diferentes dão pseudônimos diferentes. Quem precisa juntar essas grafias normaliza antes, sabendo que a
  normalização dele fica fora do contrato do Incognito.
- A injetividade do e-mail é probabilística depois da normalização: grafias que diferem só na caixa dentro da regra
  dão o mesmo pseudônimo, de propósito, e colidem numa coluna de chave única com colação que diferencia maiúsculas.
- Quem escreve um domínio ou ambiente com maiúscula, acento ou espaço recebe erro, em vez de uma chave diferente.
- Documento com dígito de outro alfabeto ou letra fora de `A`–`Z` segue a política de entrada malformada, que por
  padrão falha (ADR 0014).
- Cada regra é escrita duas vezes, no C# e no Python. Ampliar uma regra (outra letra, outra forma) é versão nova.
- O resíduo do item 7: se um runtime futuro deixar de tratar como espaço um caractere de hoje, os vetores acusam; se
  passar a tratar como espaço um caractere novo, só um vetor com esse caractere acusa.
- Nenhum teste proíbe as APIs do item 1 no código: a revisão confere, e os vetores e a referência pegam a mudança de
  saída.

## Evidência e verificação

- Código: `PseudonimizadorEmail` (regra de minúsculas em `ParaMinusculas`, comentada como congelada),
  `Subchaves.ValidarNome`, `PseudonimizadorChavePix` (minúsculas só ASCII), `PseudonimizadorChaveDfe`
  (`Ascii.EqualsIgnoreCase` no prefixo do `Id`) e `Caracteres` (só dígitos ASCII). Na data deste ADR, o
  código do núcleo não chama `ToLower`, `ToUpper`, `Normalize`, `CompareInfo` nem `TextInfo`, e as únicas chamadas a
  tabela Unicode são as do item 7.
- E-mail: `PseudonimizadorEmailTestes` (`Minusculas_seguem_a_regra_congelada`,
  `Maiusculas_e_espacos_das_pontas_nao_mudam_o_pseudonimo`, `Grafias_que_diferem_so_na_caixa_dao_o_mesmo_pseudonimo`,
  `Endereco_com_acento_e_pseudonimizado`, `Surrogate_sem_par_e_malformado`,
  `Pontos_e_mais_da_parte_local_ficam_como_vieram`).
- Nomes: `SubchavesTestes` (`Nome_fora_da_regra_e_recusado_sem_tocar_o_destino`, com acento em NFC e em NFD,
  maiúscula, espaços, largura zero, BOM, quebra de linha, caractere nulo, surrogate isolado e largura total;
  `Nome_dentro_da_regra_e_aceito`; `Variante_desconhecida_e_recusada`) e
  `PseudonimizadorTestes.Ambiente_fora_da_regra_dos_nomes_e_recusado_sem_o_valor`.
- Documentos: `ValidadorBrTestes` (`Digito_de_outro_alfabeto_e_recusado_mesmo_quando_o_DV_bate`,
  `Cnpj_com_letra_minuscula_e_recusado`), `PseudonimizadorCnpjTestes.Minuscula_valida_em_maiusculas_e_malformada`,
  `PseudonimizadorChavePixTestes` (`CNPJ_alfanumerico_em_minusculas_sai_igual_ao_em_maiusculas`,
  `Chave_aleatoria_ignora_a_caixa_e_preserva_o_preenchimento`).
- Vetores fixos: no `email@1.json`, os ramos `latin1`, `maiusculas`, `fora-da-regra` (`×`, `ß`, `Ÿ`, `İ` e o sinal de
  Kelvin), `composicao` (`é` composto e decomposto), `espacos-nas-pontas` (todos os caracteres que o
  `char.IsWhiteSpace` reconhece hoje), `espaco-no-meio`, `controle`, `controle-c1` e `surrogate-sem-par`; o ramo
  `digito-nao-ascii` nos vetores de CPF, CNPJ, telefone e CEP; o ramo `letra-nao-ascii` nos de CNPJ e de chave Pix
  (sinal de Kelvin nos dois; `ı` sem ponto na chave Pix); no `br.cep@1.json`, os ramos `entidade-aparada`,
  `entidade-acentuada`, `entidade-caixa` e `entidade-surrogate-sem-par`; as subchaves em `derivacao@1.json`.
- Referência em Python (`testes/referencia/conferir_vetores.py`): a regra de minúsculas do e-mail e os conjuntos de
  espaços e de controles escritos como listas de códigos, sem biblioteca de Unicode.
- CI: os vetores rodam no job `rapido` (Linux), exigido no pull request, e no `nucleo-so` (Windows e macOS,
  informativo).

## Gatilho de revisão

- Uma versão nova do runtime que mude o que `char.IsWhiteSpace` ou `char.IsControl` reconhecem, ou uma divergência
  entre o C# e a referência em Python nesses conjuntos.
- Pedido para juntar grafias hoje diferentes (formas Unicode, letras de outros alfabetos): vira versão nova do
  transformador.
- O primeiro transformador que produz texto, começando pelo de nome completo: conferir que segue o item 8 antes dos
  vetores dele.
- O validador da configuração do motor, quando existir: conferir que ele recusa os nomes pela mesma regra do núcleo,
  sem normalizar antes.
