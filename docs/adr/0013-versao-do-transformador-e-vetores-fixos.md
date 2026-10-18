# ADR 0013 — Versão do transformador fixada na configuração e vetores fixos como contrato

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (permutação e derivação), [ADR 0004](0004-modos-efemero-e-reproduzivel.md) (modo `reproduzivel`), [ADR 0014](0014-entrada-invalida-preservando-a-invalidade.md) (desfechos que os vetores congelam), [ADR 0019](0019-chave-de-acesso-de-dfe.md) (chave de DF-e), ADR 0012 (atualização normativa, a registrar), ADR 0018 (lançamento único `1.0.0`, a registrar)

## Contexto

No modo `reproduzivel` (ADR 0004), o valor do Incognito é que o mesmo valor, com a mesma chave, domínio e ambiente, dê
o mesmo pseudônimo em qualquer execução, inclusive em execuções com versões diferentes do pacote: uma equipe copia a
base num mês com uma versão e meses depois com outra, e as junções entre as duas cópias, ou com dados derivados da
primeira, continuam batendo. Qualquer mudança na saída de um transformador quebra isso em silêncio: a correção de um
defeito, uma regra normativa nova, outro número de rodadas da permutação, uma forma de entrada aceita a mais, outra
ordem entre os desfechos.

As saídas vêm de código criptográfico e de dígitos verificadores (HKDF, Feistel com HMAC, módulo 11), em que um erro de
ordem de bytes ou de codificação não aparece nos testes de propriedade: a saída errada continua com DV válido e
continua injetiva, só é outra. Um teste que compara o C# com ele mesmo também não pega o erro, porque o valor esperado
sai do mesmo código.

Há duas perguntas: como quem usa diz qual comportamento quer, e como o projeto prova que ele não mudou. Até a `1.0.0`
nada vai ao NuGet: ninguém tem saídas guardadas de uma versão publicada.

## Decisão

### Versão

1. Todo transformador tem nome e versão (`br.cpf@1`), e a configuração fixa a versão por domínio ou por coluna.
   Transformador escrito sem versão é a versão 1, para sempre, nunca "a mais recente".
2. A versão entra na derivação das subchaves (`dominio@versao` no `info` do HKDF, ADR 0003): duas versões do mesmo
   transformador nunca compartilham subchave, e as saídas delas não têm relação, mesmo com o mesmo algoritmo.
3. Dentro de uma versão, a saída de uma entrada, com a mesma chave, domínio, ambiente e parâmetros, nunca muda: nem o
   valor, nem o desfecho, nem o código de erro. Mudança que altere qualquer um deles (algoritmo, rodadas, tabelas,
   formas aceitas, precedência entre desfechos, regra normativa) cria uma versão nova (`br.cpf@2`); a anterior continua
   disponível, marcada como obsoleta, e o planejamento da cópia avisa quando existe versão nova.
4. Nos compostos, cada parte fica na versão do seu componente: a chave Pix usa os domínios e versões das colunas de
   CPF, CNPJ, e-mail e telefone, e a chave de DF-e usa os de CPF e CNPJ no emitente; a versão do `br.chave-dfe` só entra
   no domínio `dfe-numero` (número e código). Uma versão nova de `br.cpf` só muda o emitente da chave quando a
   configuração aponta o CPF da chave para ela.
5. A fachada `Pseudonimizador` usa a versão 1 de cada transformador em toda a série 1.x. Uma versão nova chega à
   fachada só por membro novo, sem mudar os existentes.

### Vetores fixos

6. Cada transformador e versão tem um arquivo em `testes/vetores/` (`transformador@versao.json`) com a chave pública de
   teste (que nunca deve ser usada fora dos testes), os parâmetros de cada caso e, em cada par, a entrada, a saída, o
   desfecho ou o código de erro, o ramo exercitado e a origem da entrada (semente e método do `GeradorBr`, regra de
   construção, permutação inversa com a chave de teste, ou borda literal sem documento válido). Completam o contrato a
   derivação (`derivacao@1.json`), a permutação (`feistel@1.json`) e o resumo SHA-256 de milhares de saídas por
   transformador (`volume@1.json`).
7. **Vetor só cresce.** Os casos existentes, e o cabeçalho do arquivo, nunca mudam; casos novos só se acrescentam. Um
   arquivo de vetores nunca é apagado nem editado para fazer um teste passar.
8. **Duas implementações conferem os vetores.** O C# (projeto de testes) e uma referência em Python, só com a biblioteca
   padrão, escrita a partir da descrição de cada transformador e das normas citadas: a estrutura dos algoritmos pode
   seguir a do C#, mas tabelas e constantes vêm das normas, nunca copiadas do C#. Uma
   divergência é achado a investigar, não vetor a regerar.
9. Os vetores só são gerados localmente, por um teste pulado salvo com a variável `INCOGNITO_GERAR_VETORES`, recusado
   no CI e sob a mutação; o gerador reconfere o que já existe antes de acrescentar.
10. **Exceção antes do congelamento da API.** Até o congelamento da API pública, que antecede a `1.0.0`, um caso da
    versão 1 pode mudar, porque nenhuma saída foi publicada: só com um ADR próprio, que registre o motivo, os arquivos e
    os casos afetados, e com aprovação explícita do mantenedor no pull request. A guarda do CI não tem modo de exceção,
    o script dela vem da base e o Ruleset não tem bypass: o mesmo pull request traz, além do vetor e do ADR, um patch
    versionado com os casos aprovados e o ajuste explícito do passo da guarda no `ci.yml` para aplicá-lo a uma cópia da
    base (`git worktree` do primeiro pai) antes de conferir. Assim a guarda continua valendo para todos os outros casos,
    e o pull request seguinte desfaz o ajuste e apaga o patch. Depois do congelamento, não há exceção.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Sempre a versão mais recente, sem versão na configuração | Atualizar o pacote mudaria a saída em silêncio, e as junções entre cópias feitas com versões diferentes deixariam de bater |
| Versão do pacote como versão da saída | Toda versão do pacote mudaria todas as saídas, inclusive as dos transformadores que não mudaram, e quem precisa manter os pseudônimos ficaria preso a uma versão do pacote |
| Corrigir defeito dentro da mesma versão | Quem já tem cópias com a saída antiga perde as junções com as novas sem saber; a correção vira versão nova |
| Conferir só no C#, por instantâneos SHA-256 das saídas | O que o C# calcula errado vira contrato sem ninguém ver, e um instantâneo diz que algo mudou, não o quê. A referência independente pega erros de codificação e de ordem de bytes; não pega um erro de interpretação repetido nas duas, o que fica declarado |
| Guarda rodando o script da própria pull request | Uma pull request poderia afrouxar a guarda que a julga; o CI roda a versão do script que está na base |
| Nenhuma exceção antes da `1.0.0` | Um erro achado antes de qualquer publicação, inclusive pela revisão externa da criptografia, obrigaria a carregar para sempre uma versão 1 que ninguém usou |

## Consequências

- Uma correção que muda saída vira versão nova, e a anterior continua mantida, com o defeito, enquanto durar a versão
  maior do pacote. O código e os vetores só crescem.
- Toda versão precisa existir nos dois lados: executor e esquema no C#, parâmetros e conferidor no Python. Um arquivo
  de versão sem implementação é recusado, em vez de conferido com o algoritmo de outra versão.
- A proveniência dos vetores depende da sequência do `GeradorBr` por semente: a partir da `1.0.0`, mudar a ordem dos
  sorteios, as tabelas ou as janelas do gerador só numa versão maior do pacote, com registro no CHANGELOG; antes dela,
  só pela exceção do item 10.
- As precedências que os vetores registram (por exemplo, na chave de DF-e, a forma antes da chave zerada, a
  classificação antes dos modelos aceitos e estes antes do cDV) ficam presas à versão 1.
- A guarda lê os argumentos com o script da base: mudar a linha de comando dela exige dois pull requests (o primeiro
  aceita os dois jeitos). Uma mudança no próprio `ci.yml` ainda pode afrouxá-la, e isso fica com a revisão.
- O CI precisa de Python 3.12 ou mais novo no job `rapido`.
- Uma mudança normativa nunca altera a saída em silêncio: se o resultado precisar mudar, nasce uma versão nova
  (ADR 0012).

## Evidência e verificação

- Vetores: `testes/vetores/` com `br.cpf@1`, `br.cnpj@1`, `br.telefone@1`, `email@1`, `br.cep@1`, `id.permutar@1`,
  `br.chave-pix@1`, `br.chave-dfe@1`, `derivacao@1`, `feistel@1` e `volume@1`.
- C#: `VetoresFixosTestes` (`A_pasta_de_vetores_tem_exatamente_os_arquivos_esperados`,
  `Os_casos_do_arquivo_sao_os_da_receita_na_mesma_ordem`, `Vetor_fixo_confere`, `As_sementes_crescem_em_todo_o_arquivo`,
  `A_fachada_da_o_mesmo_que_os_vetores_com_os_dominios_padrao`), `ProvenienciaDosVetoresTestes.Toda_entrada_se_refaz_a_partir_da_origem`,
  `VolumeDosVetoresTestes.O_resumo_das_saidas_nao_muda` e `GeracaoDeVetoresTestes.Gerar_os_vetores_pedidos` (recusado
  com a variável `CI`). A versão na derivação: `SubchavesTestes.Dominios_versoes_variantes_e_ambientes_diferentes_dao_chaves_de_dominio_diferentes`
  e `PseudonimizadorCpfTestes.Mesma_configuracao_da_o_mesmo_pseudonimo_e_ambiente_dominio_ou_versao_diferente_nao`. A
  sequência do gerador: `GeradorBrTestes.Sequencia_fixa_da_semente_42`.
- Python: `testes/referencia/conferir_vetores.py` confere antes as âncoras de `testes/ancoras/` (RFC 4231, RFC 5869 e os
  exemplos didáticos do DV do CNPJ alfanumérico), depois todos os vetores; `--autoteste` prova que uma saída, um
  desfecho, um código de erro ou um caso alterado são acusados pelo motivo certo; `--base REF --so-crescimento` é a
  guarda.
- CI (`.github/workflows/ci.yml`), no job `rapido`, exigido pelo Ruleset da `develop` e da `main`: a guarda roda o script
  do primeiro pai (`git show HEAD^1:testes/referencia/conferir_vetores.py`, com `--raiz`, `--base HEAD^1` e
  `--so-crescimento`); depois `conferir_vetores.py --autoteste`; depois o `dotnet test` com os testes C#. O job
  `nucleo-so` (Windows e macOS, informativo) roda os mesmos testes C#, e o `release.yml` os roda antes de empacotar.
- A versão na configuração (sem versão é a 1, aviso de versão nova, versão obsoleta) é verificada quando o motor de
  cópia existir, nos testes da validação da configuração; eles entram numa seção "Atualizações" deste ADR.
- O fluxo para acrescentar casos está na seção "Vetores fixos" do `CONTRIBUTING.md`.

## Gatilho de revisão

- O congelamento da API pública: a exceção do item 10 expira, e isso entra numa seção "Atualizações".
- A primeira versão 2 de um transformador, para conferir que a convivência das versões funciona na configuração, nos
  vetores e na referência.
- Uma divergência entre o C# e a referência em Python que se revele erro de interpretação nas duas.

## Atualizações

- 2026-10-17: o registro de transformadores (contrato do [ADR 0017](0017-contratos-de-extensao-e-tipos-do-modelo.md))
  resolve a referência sem versão escrita para a versão 1 mesmo com uma versão mais nova registrada, e recusa a
  referência sem versão quando o transformador não tem a versão 1, pedindo que a versão seja escrita
  (`ResolucaoDeRegrasTestes.Transformador_sem_versao_escrita_resolve_sempre_para_a_versao_1` e
  `Sem_versao_escrita_e_sem_a_versao_1_o_erro_pede_a_versao`). O registro já sabe a versão mais recente de cada nome;
  o aviso de versão nova e a versão obsoleta entram com o `planejar` e com a primeira versão 2.
- 2026-10-17: as fábricas embutidas rodam todos os casos dos vetores de `br.cpf@1`, `br.cnpj@1`, `br.telefone@1`,
  `email@1`, `br.cep@1` e `texto.substituir@1`, com o domínio, as políticas e os parâmetros de cada caso, numa sessão
  por ambiente, e dão o mesmo valor, desfecho e código de erro (`FabricasContraOsVetoresTestes`). Novo vetor `texto.substituir@1`, com o parâmetro
  `comprimentoMaximo` (inteiro positivo ou nulo) nos dois lados, no executor e no esquema em C# e na referência em
  Python, de 1 a 2.147.483.647; sem caso de erro possível, o autoteste prova que um comprimento máximo alterado é
  acusado e que 0, negativo, acima do limite, lógico e texto são recusados.
- 2026-10-18: as fábricas de `br.chave-pix@1` e `br.chave-dfe@1` também rodam todos os casos dos vetores delas, com a
  tabela de domínios da sessão montada dos `componentes` de cada caso, uma sessão por caso, e dão o mesmo valor,
  desfecho, código de erro e, na chave de DF-e, as mesmas ocorrências (`FabricasContraOsVetoresTestes`, que confere
  também que todos os pares do arquivo foram rodados). Nenhum vetor mudou.
