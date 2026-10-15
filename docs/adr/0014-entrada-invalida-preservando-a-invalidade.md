# ADR 0014 — Entrada inválida tratada preservando a invalidade

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (permutação e cycle-walking), [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (vetores que congelam os desfechos), [ADR 0019](0019-chave-de-acesso-de-dfe.md) (chave de DF-e), ADR 0010 (falha segura para coluna suspeita sem regra, a registrar)

## Contexto

Uma coluna real de documentos tem mais que documentos válidos:

- **DV errado**: erro de digitação, migração antiga, massa de teste esquecida. Na maior parte das vezes é o documento
  real de uma pessoa com um dígito trocado.
- **Valor fora da forma**: tamanho errado, letras, separadores trocados, CPF que perdeu o zero à esquerda numa coluna
  numérica ou numa planilha.
- **Valor de preenchimento**: texto em branco no lugar de NULL, telefone `(11) 0000-0000`, CEP `00000-000`, chave de
  acesso de 44 zeros.
- **Valor que não é de pessoa**: números de serviço 0800 e afins, cujo assinante a Anatel registra com razão social e
  CNPJ, e os de tarifa única 3003 e 4004, vendidos a empresas.

Para cada caso a ferramenta escolhe entre pseudonimizar, devolver como veio, gravar nulo ou parar, sem vazar o valor,
sem quebrar chaves primárias, restrições de unicidade e chaves estrangeiras, e sem esconder da homologação os problemas
de qualidade de dado que os testes de lá precisam encontrar.

Recalcular o DV, o caminho óbvio, faz as 100 entradas de uma mesma base com DVs diferentes caírem numa só saída (colide
e junta pessoas diferentes nas junções) e transforma inválido em válido (um teste de validação deixa de ver o defeito).
Copiar o valor como está vaza o documento. Uma versão anterior do desenho copiava em claro, por padrão, todo valor
fora da forma, e um CPF guardado sem o zero à esquerda ia para o destino como estava.

## Decisão

1. **DV errado preserva a invalidade.** Num documento de estrutura correta e DV errado (CPF, CNPJ numérico ou
   alfanumérico, cDV da chave de DF-e), `δ = (DV_entrada − DV_correto(base)) mod 100` (mod 10 no cDV), e a saída é
   `P(base) ‖ ((DV_correto(P(base)) + δ) mod 100)`. Válido continua válido; inválido continua inválido, com o mesmo
   deslocamento. Para cada base é uma translação em Z₁₀₀ composta com a bijeção P: os 100 DVs de uma base dão 100
   saídas diferentes, e a injetividade se mantém.
2. **Política `entradaInvalida`**, só onde há DV: `preservar` (padrão, item 1), `anular` (grava nulo, com desfecho
   próprio, separado do anulado por forma) ou `falhar` (código `INC1004`, sem o valor). Na chave de DF-e ela vale para o
   cDV; o DV interno do emitente, se errado, é sempre mantido pelo deslocamento (δ mod 100) e contado, sem a política da
   coluna (ADR 0019). Na chave Pix, o tipo CPF ou CNPJ segue a política da instância do componente; 11 dígitos são
   sempre CPF; CNPJ com minúsculas só é CNPJ se o DV conferir depois de passá-las a maiúsculas (senão, não tem tipo e
   segue a política de entrada malformada). Telefone, e-mail, CEP e `id.permutar` não têm DV nem essa política.
3. **A política de DV vem antes do ponto fixo.** A base excluída da permutação (CPF de base repdígito, CNPJ de raiz
   `00000000` com qualquer ordem) com DV certo é ponto fixo em toda política; com DV errado, segue a política, e
   `preservar` a devolve como veio.
4. **Entrada malformada falha por padrão.** Política `entradaMalformada`: `falhar` (padrão, código `INC1003`, sem o
   valor), `anular` ou `preservar`, que copia em claro e só vale escrita na configuração, com aviso no relatório; na
   chave Pix, `preservar` exige justificativa escrita. Antes de decidir, os zeros à esquerda perdidos são completados
   (CPF em texto com 9 ou 10 dígitos, CNPJ com 12 ou 13, CEP com 7, e os valores das colunas numéricas dentro da faixa
   do documento), e a saída sai com o tamanho completo, tirando os zeros acrescentados do preenchimento à direita de
   `char(n)`, quando houver.
5. **Valores sem dado de pessoa voltam como vieram, antes das políticas, cada um com o seu desfecho:**

   | Desfecho | Valores |
   | --- | --- |
   | `Vazio` | Texto vazio ou só com espaços, em todos os tipos |
   | `Zerado` | Telefone só com zeros (1 a 13 dígitos) ou com o número do assinante zerado depois do 55, do 0 de tronco e do DDD, só com separadores de telefone, até 32 caracteres, e o 0 em coluna numérica; CEP só com zeros (1 a 8 dígitos, `00000-000`, `00.000-000`) e o 0; chave de acesso de 44 zeros, em qualquer forma aceita (inclusive com prefixo de `Id` conhecido) |
   | `ServicoPreservado` | Telefone 0800, 0300, 0303, 0500 e 0900 (11 dígitos, ou 10 no formato antigo, com 55 ou +55 opcional e qualquer formatação; em coluna numérica, 10 dígitos, e 9 nos antigos, menos o 0900, que seria celular sem DDD) e fixo que começa com 3003 ou 4004; nenhum fixo pseudonimizado vira número de serviço |
   | `PontoFixo` | CPF de base repdígito (inclusive o zerado), CNPJ de raiz `00000000` (inclusive o zerado), identificador zero ou negativo no `id.permutar`, chave de DF-e cujas partes ficam todas como vieram |

6. **Precedência por tipo**, registrada nos vetores da versão 1:
   - CPF e CNPJ: vazio, forma, DV, ponto fixo, permutação.
   - Telefone: vazio, zerado, forma, serviço, permutação.
   - CEP: vazio, zerado, forma e faixa da UF, pseudônimo.
   - Chave de DF-e: vazio, forma (tamanho, prefixo de `Id` conhecido, grupos), chave zerada, classificação do emitente
     (anomalia é malformada), modelos aceitos e prefixo do modelo, cDV, partes. cDV errado numa chave malformada segue a
     política de entrada malformada.
7. **Desfechos contados, nunca valores.** Cada valor tratado tem um desfecho: `Pseudonimizado`, `DvInvalidoPreservado`,
   `PontoFixo`, `AnuladoDvInvalido`, `AnuladoMalformado`, `CopiadoEmClaro`, `ServicoPreservado`, `Vazio` ou `Zerado`. O
   relatório do motor de cópia conta os desfechos por coluna; nenhuma mensagem, exceção ou log traz o valor, só tabela,
   coluna e número da linha.
8. **Fachada `Pseudonimizador`:** políticas fixas, `entradaInvalida: preservar` e `entradaMalformada: falhar`
   (`INC1003`, com a mensagem do método e sem o valor); valor nulo lança `ArgumentNullException`; vazio volta como veio;
   nunca devolve nulo; o desfecho não é exposto.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Recalcular o DV | As 100 entradas de uma base com DVs diferentes caem numa só saída: colide em restrições de unicidade e junta pessoas nas junções. E inválido vira válido, escondendo da homologação o defeito do dado |
| Anular sempre o DV errado | Perde o valor em colunas `NOT NULL` e em chaves estrangeiras, e apaga o defeito que a homologação precisa ver. Continua disponível como `anular` |
| Copiar em claro o DV errado | O DV errado costuma ser o documento real de alguém com um dígito trocado: a base iria em claro |
| Falhar no DV errado por padrão | Um único valor digitado errado interromperia a cópia da base inteira, quando existe uma saída segura (item 1) que não vaza. Continua disponível como `falhar` |
| Copiar em claro o malformado por padrão | Contra a falha segura: o CPF que perdeu o zero à esquerda, comum em colunas numéricas e planilhas, ia para o destino como estava |
| Tratar zerado, vazio e serviço como malformados | Com `falhar`, uma coluna com valores de preenchimento abortaria a cópia, e a saída fácil seria `preservar`, que copia em claro todo valor malformado. O número de serviço é de pessoa jurídica e não precisa de pseudônimo |

## Consequências

- O deslocamento do DV aparece na saída: quem vê o destino sabe que o original tinha DV errado e por quanto. Ele não
  indica a base, porque para cada base existe exatamente uma entrada com cada deslocamento; a exceção declarada é o
  CNPJ de raiz `000xxxxx` lido como CPF na chave de DF-e, em que a lista curta desses CNPJs reduz os candidatos por
  cerca de 100 (ADR 0019).
- Uma coluna classificada como CPF por engano (códigos internos de 11 dígitos, por exemplo) sai quase toda como
  `DvInvalidoPreservado`: pseudonimizada, não em claro. Em coluna numérica, todo número de 0 a 99.999.999.999 é tratado
  como CPF, enquanto o mesmo valor curto em texto é malformado. Um aviso para taxa alta de DV errado numa coluna fica
  para depois.
- Os valores devolvidos como vieram (`Vazio`, `Zerado`, `ServicoPreservado`, `PontoFixo`) aparecem no destino iguais à
  origem. Nenhum é dado de pessoa, mas quem vê o destino sabe que eram de preenchimento, de serviço ou excluídos da
  permutação. A contagem de DV errado não inclui o ponto fixo com DV errado, que sai como `PontoFixo`.
- `preservar` de entrada malformada copia o valor em claro: é um risco aceito só quando escrito na configuração, e o
  relatório o aponta. Com `anular`, a bijeção vale sobre as saídas não nulas.
- Quem usa a fachada não vê o desfecho: um CPF de base repdígito volta igual sem aviso. A documentação da fachada lista
  os valores que voltam como vieram; contagens ficam com o motor de cópia.
- A lista de números de serviço é fechada e vem de norma (Res. Anatel 749/2022, art. 18; Ato Anatel 12.712/2024,
  item 7.4) ou de uso comercial declarado (3003 e 4004); outros prefixos de tarifa única são permutados como fixo comum
  até haver fonte oficial. Ampliar a lista, ou criar outro valor de preenchimento, muda a saída e exige versão nova
  (ADR 0013).

## Evidência e verificação

- DV preservado: `PseudonimizadorCpfTestes` (`CPF_com_DV_invalido_continua_invalido_com_o_mesmo_deslocamento_em_texto_mascara_e_numero`,
  `Os_100_DVs_de_uma_base_dao_100_saidas_diferentes`, `DV_invalido_segue_a_politica`,
  `Base_repdigito_e_ponto_fixo_e_a_politica_de_DV_vem_antes`) e os equivalentes de `PseudonimizadorCnpjTestes`
  (`CNPJ_de_raiz_zero_continua_igual_e_e_ponto_fixo_e_a_politica_de_DV_vem_antes`).
- Chave de DF-e: `PseudonimizadorChaveDfeTestes` (`cDV_errado_segue_a_politica_de_entrada_invalida`,
  `A_politica_de_DV_da_coluna_nao_vale_dentro_da_chave`, `Chave_zerada_volta_como_veio_em_toda_politica`,
  `Malformado_com_cDV_errado_segue_a_politica_de_malformado_e_nao_a_de_DV`).
- Chave Pix: `PseudonimizadorChavePixTestes` (`CNPJ_em_minusculas_com_DV_errado_fica_sem_tipo`,
  `CNPJ_em_maiusculas_com_DV_errado_segue_a_politica_de_DV_do_CNPJ`, `Valor_sem_tipo_segue_a_politica_da_chave_Pix`).
- Malformado, vazio, zerado e serviço: `Entrada_malformada_segue_a_politica` nos testes de CPF, CNPJ, e-mail e CEP
  (no telefone, `Entrada_escrita_malformada_segue_a_politica` e `Entrada_montada_malformada_segue_a_politica`; na chave
  de DF-e, `Formas_malformadas_seguem_a_politica_sem_o_valor_na_mensagem`; no `id.permutar`,
  `Identificador_a_partir_de_N_segue_a_politica` e `Zero_e_negativos_voltam_como_vieram_em_toda_politica`);
  `Texto_vazio_ou_em_branco_volta_como_veio_em_toda_politica` nos testes de CPF, CNPJ, telefone, e-mail, CEP e chave
  Pix; `PseudonimizadorTelefoneTestes` (`Numeros_de_servico_voltam_como_vieram`,
  `Telefone_so_com_zeros_volta_como_veio_em_toda_politica`, `Numero_com_o_numero_do_assinante_zerado_volta_como_veio`,
  `Servico_numerico_que_perdeu_o_zero_volta_como_veio`); `PseudonimizadorCepTestes.CEP_so_com_zeros_volta_como_veio_em_toda_politica`.
- Fachada: `PseudonimizadorTestes` (`DV_errado_e_mantido_pelo_deslocamento_e_continua_errado`,
  `Malformado_lanca_INC1003_sem_o_valor`, `Pontos_fixos_prometidos_voltam_como_vieram`,
  `Vazio_volta_como_veio_e_nulo_e_recusado`).
- Vetores fixos (ADR 0013): os arquivos de `testes/vetores/` têm pares com cada desfecho e cada código de erro, com o
  ramo exigido de cada um, conferidos pelo C# e pela referência em Python (`testes/referencia/conferir_vetores.py`), no
  job `rapido` do CI; o `volume@1.json` mistura DV certo e deslocado em milhares de CPFs e CNPJs.
- As contagens no relatório e o aviso de `preservar` são verificados quando o motor de cópia existir; os testes entram
  numa seção "Atualizações" deste ADR.

## Gatilho de revisão

- O relatório do motor de cópia passar a contar os desfechos: conferir que a separação entre `AnuladoDvInvalido` e
  `AnuladoMalformado`, e entre `Zerado`, `ServicoPreservado` e `PontoFixo`, serve a quem lê.
- Mudança na numeração da Anatel (novos códigos não geográficos, ou a numeração do SCM, prevista para 2027) ou fonte
  oficial para outros prefixos de tarifa única.
- Pedido de política própria para valores de preenchimento, ou de expor o desfecho na fachada.
