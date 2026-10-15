# ADR 0003 — Permutação Feistel modular com HMAC e HKDF

- Status: aceito
- Data: 2026-10-13
- Relacionados: [ADR 0019](0019-chave-de-acesso-de-dfe.md)

## Contexto

Pseudonimizar uma coluna usada como chave primária ou estrangeira (CPF, CNPJ, telefone, número de nota,
identificador) exige uma função que leve cada valor a outro do mesmo formato, sempre o mesmo para a mesma chave do
Incognito, domínio, versão e ambiente, e nunca o mesmo para valores diferentes: duas pessoas que colidissem quebrariam
chaves primárias e estrangeiras. Os espaços de valores são decimais ou em base 36, de tamanhos que não são potência de
2 e às vezes minúsculos (o espaço de 36 valores do dia e os sequenciais de 10⁴ e 10⁵ da Nota Fiscal Fácil). Vários
precisam de ajuste (o DDD do telefone, o emitente já pseudonimizado na chave de DF-e) e de valores excluídos da saída
(repdígitos do CPF, códigos numéricos proibidos, a raiz `000xxxxx` na regra dos zeros do CT-e).

Quem tem a chave reverte tudo: a ferramenta não promete anonimização, e a chave fica fora do destino. O objetivo da
função é limitado: para quem não tem a chave nem conhece pares de entrada e saída, a saída de um valor isolado não deve
indicar a entrada nem a ordem dela; e o custo por valor precisa caber numa cópia de banco.

## Decisão

1. A permutação é uma rede de Feistel modular sobre o espaço Z_a × Z_b, no estilo do FF1 (NIST SP 800-38G), mas com
   lados a e b quaisquer em vez de potências do radix e com HMAC-SHA256 na função de rodada: `L = x div b`,
   `R = x mod b`; na rodada i (i = 0, 1, …), `C = (L + F_i(R)) mod m_i`, com `m_i` = a nas rodadas pares e b nas
   ímpares; depois `L = R`, `R = C`; saída `L·b + R`. O lado maior é no máximo radix vezes o menor, como no FF1.
2. A função de rodada é `F_i(v) = U64BE(HMAC-SHA256(K_dominio, "R" ‖ u8(i) ‖ u32be(radix) ‖ u64be(a) ‖ u64be(b) ‖
   u16be(|ajuste|) ‖ ajuste ‖ u64be(v))[0..8]) mod m_i`, com `K_dominio` do item 4 e uma instância de HMAC reaproveitada
   por thread. Radix, a e b na mensagem separam espaços de tamanhos diferentes que usem a mesma chave de domínio.
3. São 12 rodadas, e 24 quando min(a, b) < 10 (hoje só o dia da NFF, 6 × 6). A permutação exige número par, de 10 a
   254; as 24 rodadas em lado pequeno são regra dos pseudonimizadores. Cada chave de domínio é usada com um único
   número de rodadas.
4. Cada domínio, variante, versão e ambiente tem a sua chave, derivada por HKDF-SHA256 (RFC 5869) da chave do
   Incognito.
5. A saída evita um conjunto E de valores excluídos (no máximo metade do espaço, contando um valor adicional por
   chamada, como o número já pseudonimizado no código da chave de DF-e) por cycle-walking: aplica-se a permutação de
   novo enquanto a saída cair em E; uma entrada em E fica como veio. O limite de passos é min(|E| + 1, 64); estourá-lo
   lança `INC1002`.
6. O gatilho para trocar a função de rodada é medido por permutação: 12 rodadas, ajuste de 20 bytes, mediana do job
   padrão do BenchmarkDotNet, na máquina de referência declarada abaixo (um processador lógico de um núcleo P). Acima
   de 5 µs, a troca é decidida com o benchmark de ponta a ponta das cópias, quando o motor de cópia existir, sem mudar
   os vetores antes disso. Medido: 2,02 µs. A permutação de 24 rodadas e os transformadores compostos são reportados à
   parte.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| HMAC truncado para o número de dígitos | Não é injetivo: truncar para 9 dígitos colide pelo paradoxo do aniversário (ordem de 500 colisões em 1 milhão de CPFs). |
| FF1 (NIST SP 800-38G e o rascunho da Rev. 1) | Exige espaço radix^n e, desde o primeiro rascunho da Rev. 1 (2019), motivado pelos ataques a espaço pequeno, ao menos 10⁶ valores. O segundo rascunho, de fevereiro de 2025, aumenta esse mínimo, segundo o resumo das mudanças publicado pelo NIST. O dia da NFF (36), o sequencial (10⁴ e 10⁵) e o `id.permutar` com N pequeno ficariam de fora, e espaços como 4000 × 10⁴ (telefone fixo) precisariam de cycle-walking extra. A construção adotada segue a estrutura do FF1 e mantém o ajuste. |
| FF3 e FF3-1 | O FF3 foi quebrado em espaços pequenos por Durak e Vaudenay (2017, ataque deslizante ao escalonamento do ajuste), e o FF3-1 tem criptoanálise linear publicada (Beyne, 2021); o segundo rascunho da Rev. 1 (2025) deixa de especificar o FF3. Não devem ser usados. |
| Função de rodada com AES (um HMAC do cabeçalho e do ajuste por valor, depois um bloco AES-256 por rodada) | Medido 2,5 vezes mais rápido (0,77 µs contra 1,90 µs por permutação, lado a lado), mas o custo atual está longe do gatilho, e a troca mudaria todas as saídas e exigiria mais código criptográfico próprio. O ganho grande só viria cifrando em lote (uma chamada por rodada para um lote de linhas), o que exige uma API de lote no motor. Fica registrada para quando o motor de cópia existir. |
| Embaralhamento explícito do dia da NFF (Fisher–Yates dos 36 valores alimentado por HMAC) | Tão forte quanto a função pseudoaleatória que o alimenta, sem a ressalva de Feistel em espaço minúsculo, e mais barato que 24 rodadas com cycle-walking; mas muda as saídas do dia, e o custo de 24 rodadas (cerca de 4 µs) só pesa nas chaves da NFF. Mantido o Feistel de 24 rodadas. |

## Consequências

- A função é determinística: o mesmo valor dá o mesmo pseudônimo em todas as linhas e tabelas do mesmo domínio (é o
  que mantém chaves estrangeiras e junções). Igualdades e frequências sobrevivem, registros podem ser ligados entre
  tabelas, e um único par conhecido de entrada e saída revela todas as ocorrências daquele valor no destino.
- O custo é de cerca de 0,17 µs por rodada na permutação (2,02 µs / 12; o HMAC-SHA256 isolado, da biblioteca do
  sistema, mede 158 ns): 2 µs por permutação de 12 rodadas e cerca de 4 µs com 24. Um transformador de documento faz
  uma permutação por valor (2 µs); a chave de DF-e faz três (5,5 a 5,9 µs) e, na NFF, quatro, uma delas de 24 rodadas
  com cycle-walking do dia (11 µs). Com 8 threads, o CPF passa de 2,0 µs (1 thread, na mesma medição) para 0,39 µs
  por valor.
- A permutação não é criptografia padronizada, e os limites formais conhecidos para Feistel com função de rodada
  pseudoaleatória (Patarin, 2004; Hoang e Rogaway, 2010) não dizem nada útil para lados de até 10². Em espaço pequeno,
  o mesmo valor (ou valores que dividem uma metade) sob muitos ajustes pode ser recuperado por ataques de recuperação
  de mensagem (Bellare, Hoang e Tessaro, 2016; Hoang, Tessaro e Trieu, 2018). Aqui isso alcança os números e códigos
  pequenos da chave de DF-e (com a metade esquerda praticamente conhecida) e o sequencial da NFF, sob milhares de
  emitentes como ajuste, para quem vê o destino e conhece alguns pares de entrada e saída. O dano é revelar a ordem ou
  o valor de números de nota; o risco é aceito e declarado.
- O número de rodadas não entra na função de rodada: mudar as rodadas de um domínio exige outra versão do
  transformador.
- O cálculo depende da biblioteca de criptografia do sistema (CNG no Windows, OpenSSL no Linux): as saídas são as
  mesmas, os tempos não.

## Evidência e verificação

- Vetores fixos em `testes/vetores/` conferidos por uma referência independente em Python (`testes/referencia/`) e
  pelo C#, inclusive caminhadas reais no cycle-walking e o resumo de milhares de saídas por transformador
  (`volume@1.json`).
- Bijeção exaustiva em espaços reduzidos (balanceados, desbalanceados nas duas ordens, base 36 particionada), e a
  propriedade P⁻¹(P(x)) = x sobre parâmetros sorteados (CsCheck: radix, lados até 2³² − 1, rodadas, ajuste de até 300
  bytes, exclusões e o valor adicional).
- Testes estatísticos de fumaça (`EstatisticaTestes`): pontos fixos e ciclos de uma permutação aleatória em espaços
  pequenos (inclusive o dia da NFF), concordância entre chaves de domínios, variantes, ambientes e versões diferentes e
  entre chaves que diferem num byte, avalanche da entrada e de cada byte do ajuste, e entradas consecutivas sem padrão.
  Cada família de conferência reprova controles ruins (identidade, deslocamento, involução, Feistel com a mesma função
  em toda rodada, multiplicação, chave repetida ou lida só em parte, ajuste ignorado, Feistel com função de rodada de
  um bit). Não provam segurança.
- Numa mutação manual do núcleo (2, 4 e 6 rodadas; função de rodada igual em toda rodada, de 1 bit ou de 16 bits;
  ajuste ignorado ou truncado; variante ou ambiente fora da derivação), o resto da suíte, sem os testes estatísticos,
  matou todos os mutantes: eles não acrescentam cobertura à implementação atual. Servem para uma versão nova, antes de
  ela ter vetores: aí pegam 2 rodadas, a função de rodada repetida ou de 1 bit, o ajuste ignorado ou truncado e a
  variante ou o ambiente fora da derivação; não pegam 4 ou 6 rodadas nem a função de rodada de 16 bits.
- Benchmark (`benchmarks/`, BenchmarkDotNet 0.15.8), em `benchmarks/resultados/2026-10-13-intel-core-i9-14900hx/`.
  Máquina de referência: Intel Core i9-14900HX, Windows 11 (plano "Equilibrado" no sistema, "Alto desempenho" aplicado
  pelo BenchmarkDotNet durante a medição), .NET SDK 10.0.401 (runtime 10.0.12), afinidade num processador lógico de um
  núcleo P. O workflow `benchmark` (manual) mede no runner do GitHub, como número informativo: a CPU do runner varia
  entre execuções.

Tempos pela mediana quando o relatório a traz e pela média nas outras linhas (o BenchmarkDotNet só mostra a
mediana quando ela se afasta da média); a medida do gatilho é mediana. Cada caso rodou num só processo, e entre
processos os tempos variam até cerca de 15%: a mesma permutação mediu 2,03 µs (média) numa classe e 1,90 µs noutra, o
`id.permutar` (1,76 µs) saiu abaixo da permutação crua que ele chama (1,97 µs), e transformadores de cerca de 2 µs não
se ordenam por estes números. O gatilho (2,02 contra 5 µs) e a razão entre AES e HMAC (2,5 vezes, medidos lado a
lado) não dependem dessa variação. A vazão com várias threads inclui o efeito do hyper-threading e da queda de
frequência com vários núcleos ativos.

| Medida | Tempo | Alocação |
| --- | --- | --- |
| Permutação, 10 / 12 / 24 rodadas, ajuste de 20 bytes | 1,66 / 2,02 / 3,97 µs | 0 |
| Permutação, 12 rodadas, ajuste de 0 / 33 bytes | 1,97 / 2,37 µs | 0 |
| Rodada: HMAC-SHA256 reaproveitado / de uma vez | 158 / 282 ns | 0 |
| Rodada: AES de um bloco reaproveitado / em lote de 1024 / `EncryptEcb` | 37 / 1,8 / 319 ns | 0 / 0 / 88 B |
| Permutação com AES por rodada (protótipo) contra HMAC | 0,77 contra 1,90 µs | 0 |
| CPF, CNPJ, telefone, `id.permutar` | 2,1 / 2,0 / 1,9 / 1,8 µs | 48 / 56 / 56 / 0 B |
| E-mail, CEP, chave Pix (tipos misturados) | 0,36 / 0,35 / 1,3 µs | 88 / 40 / 69 B |
| Chave de DF-e: NF-e / CT-e / NF3e / NFF | 5,5 / 5,9 / 5,7 / 11,0 µs | 112 B |
| `ValidadorBr`: CPF / CNPJ / chave | 20 / 24 / 37 ns | 0 |
| CPF com 1 / 4 / 8 threads (vazão por valor) | 2,0 / 0,71 / 0,39 µs | 48 B |

## Gatilho de revisão

- A permutação de 12 rodadas passar de 5 µs na máquina de referência, ou o benchmark de ponta a ponta das cópias
  mostrar os transformadores acima de 25% do tempo de uma cópia com cinco colunas tratadas.
- Ataque publicado, além dos de recuperação de mensagem já aceitos (Bellare, Hoang e Tessaro, 2016; Hoang, Tessaro e
  Trieu, 2018), que distinga a permutação ou recupere valores nos tamanhos usados sem exigir pares conhecidos, ou que
  reduza a poucos ajustes o custo daqueles ataques.
- A versão final da Rev. 1 da SP 800-38G trazer, para espaços pequenos, uma exigência (rodadas, tamanho mínimo, uso do
  ajuste) que esta construção não cumpra.
- Um domínio novo com lado menor que 10, além do dia da NFF.

## Atualizações

- 2026-10-15: conferência de regressão na preparação da versão candidata do núcleo (`v1.0.0-rc.2`), na mesma máquina
  de referência, em
  `benchmarks/resultados/2026-10-15-intel-core-i9-14900hx/`. O código medido não mudou desde 13/10; mudaram o patch
  do Windows e o do runtime (10.0.13). Com um processo por caso, 38 dos 41 casos não passaram de 10% acima de 13/10
  (mediana, ou média quando o relatório não a traz); os três acima (e-mail, `id.permutar` e vazão do CPF com 8 threads), medidos de novo com 3 processos, ficaram
  entre +0,9% e +5,0%. A permutação de 12 rodadas com ajuste de 20 bytes mediu 1,77 µs, longe do gatilho de 5 µs.
