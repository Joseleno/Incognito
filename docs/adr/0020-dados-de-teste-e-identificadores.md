# ADR 0020 — Dados de teste só sintéticos, com identificadores do gerador e âncoras didáticas

- Status: aceito
- Data: 2026-10-15
- Relacionados: [ADR 0013](0013-versao-do-transformador-e-vetores-fixos.md) (vetores fixos e origem de cada entrada), [ADR 0014](0014-entrada-invalida-preservando-a-invalidade.md) (números de serviço preservados), [ADR 0019](0019-chave-de-acesso-de-dfe.md) (chave de DF-e e gerador de chaves)

## Contexto

O Incognito trata justamente o dado que não pode aparecer no repositório: CPF, CNPJ, telefone, e-mail, CEP, chave
Pix e chave de acesso de documento fiscal eletrônico. Os testes, os vetores fixos (ADR 0013), os exemplos e a
documentação precisam de milhares desses valores, e de valores válidos: DV certo, CEP dentro da faixa da UF, chave
com modelo, série e emitente que um autorizador aceitaria. Tudo que entra no repositório é público, fica no histórico
do git e é copiado por forks e espelhos: um valor real publicado não se retira.

Os caminhos fáceis vazam:

- **Dado de produção mascarado**, ou "só alguns registros": continua dado pessoal, e a máscara costuma deixar partes
  em claro.
- **Identificadores "de exemplo" que circulam** em manuais, fóruns e ambientes de homologação: a homologação de
  órgãos e de bancos usa cadastros reais, e um CNPJ citado numa notícia é, por definição, de alguém.
- **Chaves de acesso de exemplo** dos manuais de leiaute e vetores numéricos de pacotes oficiais: são de documentos
  autorizados ou montados sobre contribuintes cadastrados.

E nenhum órgão reserva faixa de CPF, CNPJ ou telefone para testes: todo documento válido gerado pode coincidir com o
de alguém. "Sintético" quer dizer "não tirado de um cadastro, com origem registrada", não "de ninguém, com certeza".

Duas necessidades legítimas pedem exceção. A primeira é conferir o DV do CNPJ alfanumérico contra uma fonte
independente do próprio código, e a única fonte publicada são os exemplos que a Receita Federal põe no material
didático do algoritmo. A segunda é provar que os números de serviço voltam como vieram (ADR 0014): os vetores
precisam trazê-los escritos, e eles são números de empresas.

## Decisão

1. **Nunca dado real** em testes, fixtures, vetores, exemplos, documentação, issues e pull requests: só dados
   sintéticos. Regras e tabelas vêm de especificação pública (lei, norma, nota técnica, manual publicado), nunca de
   dado, sistema ou documento interno de empresa, inclusive de quem contribui.
2. **Origem registrada.** Fora as âncoras do item 5, todo identificador escrito no repositório vem de uma destas
   origens, e só delas:
   1. o `GeradorBr` com semente registrada (nos vetores, a origem `gerador-br`, com `metodo`, `semente` e `formas`);
   2. uma regra de construção sobre a sequência do mesmo sorteador com semente (origem `construido`, com `regra`,
      `semente` e `formas`), inclusive a regra `chave-dfe`, que monta a chave de acesso pelo gerador detalhado do
      projeto de testes (`GeradorChaveDfe`) a partir da especificação escrita na regra;
   3. a permutação inversa com a chave pública de teste, para alcançar ramos raros por pré-imagem (origem `inversa`,
      com `regra`, `semente` e `formas`);
   4. um literal que não é documento de ninguém (origem `literal`): texto vazio, espaços, valor fora da forma,
      valores zerados, as bordas das faixas de CEP (de logradouro, não de pessoa), as bordas numéricas do
      `id.permutar` e a chave de acesso de 44 zeros, valor de preenchimento.

   O `volume@1.json` deriva as entradas de um contador por SHA-256, com a fórmula escrita no próprio arquivo. Nos
   testes de unidade, os documentos são sorteados na hora, pelo `GeradorBr` ou por um sorteio com a semente de cada
   teste; nenhum identificador válido fica escrito no código.
3. **Nada cadastrado, nunca.** Não entram no repositório: identificador cadastrado (CPF, CNPJ, inscrição estadual),
   mesmo de homologação ou publicado como "de teste"; chave de acesso de documento autorizado, inclusive as de
   exemplo de manuais e notas técnicas; vetor numérico de pacote oficial; CNPJ citado em notícia. Faixas que se sabe
   ocupadas por cadastros só aparecem nos vetores com o DV deslocado: o CNPJ de raiz `00000000` com ordem de
   estabelecimento numérica e, no emitente da chave de DF-e, a raiz numérica começando com `00` e ordem diferente de
   `0000`.
4. **Documentação sem documento válido.** Nenhum `.md` versionado traz CPF, CNPJ ou chave de acesso com DV válido,
   nem como exemplo: o exemplo gera na hora (o do README usa o `GeradorBr`).
5. **Âncoras didáticas.** Os exemplos didáticos do CNPJ alfanumérico publicados pela Receita Federal em material do
   algoritmo (manual do DV, perguntas frequentes, códigos de referência) servem só como âncora do cálculo do DV, num
   arquivo de `testes/ancoras/` marcado com `"origem": "didatico-rfb"`, e não são usados em nenhum outro teste,
   exemplo ou documento. Exemplo numérico de pacote oficial e chave de acesso de exemplo não viram âncora.
6. **Exceção registrada: números de serviço.** Os vetores de telefone e de chave Pix trazem números de serviço com
   dígitos sorteados: 0800, 0300, 0303, 0500 e 0900 com 7 dígitos sorteados, ou 6 no formato antigo; e 3003 ou 4004
   com 4 dígitos sorteados ou nas bordas 0000 e 9999, sem DDD ou depois de um DDD inexistente. Podem coincidir com números de empresas, nunca são de pessoa, e são
   necessários para provar a preservação. A descrição desses dois arquivos de vetores diz isso. Fora as âncoras do
   item 5, não há outra exceção.
7. **Chaves.** Nenhuma chave real no repositório. As chaves públicas de teste ficam só em `testes/vetores/`, com o
   aviso de que nunca devem ser usadas fora dos testes; os outros testes as leem de lá ou geram uma na hora.
8. **Notas de pesquisa fora.** Notas brutas de pesquisa (páginas salvas, consultas, planilhas de fontes) nunca entram
   no repositório; entra só o texto revisado, sem identificador, como a referência no catálogo normativo e a citação
   da norma no código ou no ADR.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Dados reais mascarados ou um extrato pequeno de produção | Continuam dado pessoal; a máscara deixa partes em claro; uma vez no histórico do git, não se retiram |
| Identificadores "de exemplo" conhecidos, de manuais, fóruns ou homologação | Muitos são de pessoas e empresas reais; a homologação usa cadastros reais |
| Chaves de acesso de exemplo dos manuais e vetores de pacotes oficiais | São de documentos autorizados ou montados sobre contribuintes cadastrados |
| Literais "inventados" à mão | Um valor inventado com DV certo coincide com o de alguém tanto quanto um gerado, e, sem origem registrada, ninguém confere que não foi copiado nem o refaz |
| Gerar tudo na hora, sem nenhum valor gravado | Os vetores só são contrato se as entradas ficam gravadas (ADR 0013); a origem registrada permite refazê-las e conferi-las |
| Nenhuma âncora externa para o DV do CNPJ alfanumérico | O cálculo seria conferido só contra o próprio projeto; os exemplos didáticos são a única fonte independente e não são cadastro |
| Trocar os números de serviço por números fictícios | Não existe faixa de serviço reservada para teste; sem os prefixos reais, a preservação do ADR 0014 não seria provada |

## Consequências

- Acrescentar um caso aos vetores exige uma semente, uma regra ou uma pré-imagem, não um valor colado. A partir da
  `1.0.0`, a sequência do `GeradorBr` por semente fica presa à versão maior (ADR 0013), porque a proveniência refaz
  as entradas por ela.
- Um valor gerado pode coincidir com o de alguém: a documentação do `GeradorBr` e a descrição de cada arquivo de
  vetores dizem isso e pedem que os valores não sejam usados fora dos testes.
- A exceção dos números de serviço põe números de empresas no repositório. O custo é aceito porque não são de pessoa
  e porque a lista é fechada; ampliá-la exige mudar este ADR.
- O cDV com letra da chave de acesso não tem âncora externa: o único exemplo publicado é uma chave, e chave de
  exemplo não entra. O cálculo é conferido pela referência independente em Python (ADR 0013).
- Exemplos na documentação usam o gerador ou formatos sem documento válido; a varredura reprova o `.md` que trouxer
  um documento válido, mesmo por acaso.
- Issues e discussões não são versionadas e dependem de quem escreve: os formulários de bug e de melhoria pedem dados
  sintéticos, e o [`SECURITY.md`](../../SECURITY.md) proíbe dado pessoal real no relato.
- Nos testes e nos benchmarks, a chave é a pública dos vetores ou uma gerada na hora (inclusive derivada de uma
  semente); nenhuma outra fica escrita.

## Evidência e verificação

- Proveniência (`ProvenienciaDosVetoresTestes`): `Toda_entrada_se_refaz_a_partir_da_origem` refaz cada entrada não
  literal pela origem registrada e reprova o literal de CPF, de CNPJ, de chave Pix com CPF ou CNPJ válido, ou de
  chave de DF-e válida; `Nenhum_par_traz_cnpj_cadastrado_de_raiz_zero`;
  `Nenhuma_chave_de_DF_e_traz_emitente_de_raiz_baixa_com_DV_certo` (entradas e saídas);
  `Regra_da_raiz_baixa_so_recusa_raiz_00_com_ordem_de_estabelecimento_e_DV_certo`.
- Leitura estrita: o esquema do C# (`EsquemaDeVetores`) e a referência em Python
  (`testes/referencia/conferir_vetores.py`) recusam par sem origem ou com tipo fora de `gerador-br`, `construido`,
  `inversa` e `literal`.
- Descrição dos vetores: `VetoresFixosTestes.Os_casos_do_arquivo_sao_os_da_receita_na_mesma_ordem` confere que a
  descrição de cada arquivo é a da receita, inclusive o parágrafo dos números de serviço em `br.telefone@1.json` e
  `br.chave-pix@1.json`.
- Âncoras: `ValidadorBrTestes.Ancoras_didaticas_sao_marcadas_como_origem_didatico_rfb` e
  `Cnpj_confere_com_as_ancoras_didaticas_da_Receita`; a referência em Python confere as âncoras antes dos vetores.
- Documentação: `DocumentacaoTestes.Documento_nao_traz_CPF_CNPJ_nem_chave_de_acesso_validos` varre todo `.md`
  versionado (CPF e chave de acesso em qualquer janela de dígitos, mesmo com máscara ou quebra de linha; CNPJ,
  numérico ou alfanumérico, como palavra inteira) e aponta só a posição; `A_varredura_ve_a_documentacao` confere que
  a varredura enxerga os arquivos. O exemplo do README é o que `ExemploDeUsoDiretoTestes` roda
  (`ReadmeTestes.O_codigo_do_README_e_o_que_os_testes_rodam`).
- Na revisão: código (`.cs`) sem identificador válido escrito, chaves no formato `inc1_` só em `testes/vetores/`,
  nenhuma nota de pesquisa e nenhuma fonte interna. Não há teste automático para nenhum desses pontos.

## Gatilho de revisão

- Um valor no repositório que se descubra ser de pessoa real, ou cadastrado: a remoção conflita com "vetor só
  cresce" (ADR 0013) e precisa ser decidida.
- Um órgão publicar faixa reservada para testes (CPF, CNPJ, numeração telefônica), ou a Anatel mudar a numeração de
  serviço.
- Um transformador novo cujo identificador o `GeradorBr` não gera: a origem dos valores dele é definida antes dos
  primeiros vetores.
