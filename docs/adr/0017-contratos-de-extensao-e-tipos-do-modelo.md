# ADR 0017 — Contratos de extensão como classes abstratas e tipos do modelo sem construtor público

- Status: aceito
- Data: 2026-10-17
- Relacionados: [ADR 0011](0011-nucleo-sem-dependencias.md) (núcleo sem dependências; o motor não usa os internos do núcleo), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (versão do transformador e vetores fixos), [ADR 0004](0004-modos-efemero-e-reproduzivel.md) (quem é dono da chave)

## Contexto

O Incognito deixa quem o usa escrever transformadores próprios e registrá-los, e vem com os seus (`br.cpf`, `br.cnpj`,
`br.telefone`, `email`, `br.cep`, `texto.substituir`, e outros a caminho). Esse contrato vai ser usado por código de
terceiros por anos, a partir da `1.0.0`, e precisa crescer sem quebrar quem já o estende.

Ao mesmo tempo, o motor produz um modelo (a configuração lida, os erros, o plano) que só faz sentido saído dele: um
plano montado à mão, sem passar pelo `planejar`, pularia as conferências que protegem o destino.

Há ainda duas restrições de segurança. A chave e o que deriva dela (subchaves, instâncias que pseudonimizam) nunca
podem chegar a código de terceiros: quem chama um pseudonimizador monta a tabela inteira e reverte os pseudônimos. E
os valores das colunas tratadas não podem aparecer em nenhuma saída (log, relatório, exceção), mas o contrato precisa
carregá-los de um lado para o outro.

## Decisão

1. **Os contratos de extensão são classes abstratas, nunca interfaces**: `Transformador` e `FabricaTransformador`, no
   namespace `CodeProcess.Incognito.Transformadores` do núcleo. Um membro novo entra como virtual, com um padrão que
   mantém o comportamento de quem já estende.
2. **`FabricaTransformador.Criar` é um método-modelo, não virtual.** Ele confere a regra (`Validar`), a versão do
   domínio e o tipo da coluna, chama o `CriarCore` de quem estende e confere que o transformador criado tem o nome e a
   versão da fábrica. Quem estende não consegue pular a conferência.
3. **O modelo produzido pelo motor não tem construtor público** (`ConfiguracaoIncognito`, `ErroConfiguracao`,
   `ConfiguracaoInvalidaException` e, depois, `Plano` e `Relatorio`): só nasce do motor.
4. **As entradas e saídas do contrato de extensão têm construtor público**, para quem escreve uma fábrica poder testá-la
   sem o motor: `DominioPseudonimo`, `RegraTransformacao`, `ContextoCriacao`, `ContextoTransformacao`,
   `ResultadoTransformacao`, `RegraInvalidaException` e `SessaoTransformacao`. São classes seladas ou structs, e
   crescem por propriedades `init` validadas, nunca por construtor posicional com mais parâmetros.
5. **Os structs que carregam valor de coluna não são records** (`ContextoTransformacao`, `ResultadoTransformacao`): o
   `ToString` e o depurador mostram só o desfecho ou o nome do tipo, nunca o valor.
6. **Os enums do contrato ganham valores** (`TipoColuna`, `Desfecho`, `Injetividade`, as políticas), e quem os lê trata
   um valor desconhecido. O zero do `Desfecho` é `Indefinido`, que o motor recusa, para um resultado `default` nunca
   passar por sucesso.
7. **Nada derivado da chave chega a uma fábrica de terceiro na `1.0`.** A sessão (`SessaoTransformacao`) entra no
   contexto de criação como parâmetro do construtor, mas o acesso a ela é interno ao núcleo: uma fábrica de fora não a
   descarta nem cria pseudonimizadores por ela. Um contexto serve a uma fábrica só: o `Criar` o reserva para a
   primeira que o usa, e uma fábrica de fora que o repasse a uma embutida é recusada. Uma função de semente opaca
   para transformadores próprios fica para depois, como membro novo do contexto.
8. **O motor nunca registra a mensagem de uma exceção de transformador de terceiro** (nem `InnerException`, `Data` ou
   `ToString`): as exceções do .NET trazem a entrada (a `FormatException` do `int.Parse`, desde o .NET 8). Registra só o
   tipo, o código de uma `IncognitoException`, a tabela, a coluna e a linha. As mensagens de `Validar` e `Criar` podem ser
   mostradas, porque só veem configuração.
9. **Nomes reservados às fábricas embutidas**: os prefixos `br.`, `id.`, `texto.`, `data.`, `numero.`, `json.`,
   `incognito.`, `email.`, `fixo.` e `nulo.` e os nomes `email`, `fixo` e `nulo`. Uma fábrica de terceiro usa um prefixo
   da organização (`empresa.algo`), e o domínio padrão dela é o próprio nome.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Interfaces, com métodos padrão | Um método padrão de interface não guarda estado nem permite um método-modelo selado; quem implementa a interface pode pular a conferência. |
| Construtor público em todo tipo | Um `Plano` montado à mão pularia o `planejar`; os erros de configuração deixariam de ser só os do motor. |
| Records para os contextos e o resultado | O `ToString` gerado mostra o valor da coluna, o `with` contorna a validação e o construtor posicional não cresce sem quebrar. |
| Entregar a sessão (ou um pseudonimizador) à fábrica de terceiro já na `1.0` | Superfície criptográfica sem volta antes de haver quem precise; com ela, a fábrica de fora vira um oráculo de pseudônimos. |

## Consequências

- Quem estende herda de uma classe e não pode herdar de outra; é o custo de poder evoluir o contrato.
- Um transformador próprio, na `1.0`, só é determinístico sem a chave. A documentação proíbe hash sem chave para dado
  pessoal (quem conhece a forma refaz a tabela por força bruta).
- Todo campo novo dos contextos entra como propriedade `init` com padrão, e todo membro novo dos contratos, como
  virtual com padrão; mudar um membro abstrato ou o construtor de uma entrada é quebra.
- O teste de arquitetura separa os dois casos: o modelo do motor sem construtor público; as entradas do contrato com.

## Evidência e verificação

- `RegrasDeArquiteturaTestes`: `Os_contratos_de_extensao_sao_classes_abstratas_sem_construtor_publico_e_o_Criar_nao_e_virtual`,
  `Tipos_publicos_do_motor_so_nascem_do_motor`, `Os_tipos_publicos_do_contrato_sao_selados_abstratos_structs_ou_enums` e
  `Os_structs_com_valor_de_coluna_nao_sao_record_e_nao_herdam_o_ToString`.
- `ContratoDeTransformacaoTestes.Uma_fabrica_de_fora_nao_repassa_o_contexto_a_uma_embutida` (item 7).
- `ContratoDeTransformacaoTestes` (fábrica de fora pelo método-modelo, fábrica que cria outro transformador recusada,
  nada de valor no `ToString`), `FabricasEmbutidasTestes` e, no motor, `ResolucaoDeRegrasTestes` (nomes reservados,
  metadados lidos uma vez no registro, erro inesperado de uma fábrica de fora sem a mensagem dela).
- `PublicAPI.Unshipped.txt` do núcleo e do motor: toda mudança da superfície passa pela revisão.
- O item 8 no `Transformar` é verificado pelos testes da execução, quando ela existir; eles entram numa seção
  "Atualizações" deste ADR. No `Validar`, por `ResolucaoDeRegrasTestes.A_fabrica_propria_confere_a_regra_e_um_erro_inesperado_vira_recusa_sem_a_mensagem`.

## Gatilho de revisão

- O congelamento da API pública, para conferir a lista do item 4 e as propriedades de cada contexto.
- O primeiro pedido de um transformador próprio determinístico com a chave (item 7).
- Uma versão do .NET que mude o que um método padrão de interface pode fazer.
