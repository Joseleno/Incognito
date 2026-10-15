# ADR 0019 — Chave de acesso de DF-e pseudonimizada por partes, com o emitente classificado por campos preservados

- Status: aceito
- Data: 2026-10-12
- Relacionados: [ADR 0003](0003-permutacao-feistel-com-hmac.md) (permutação Feistel), [ADR 0014](0014-entrada-invalida-preservando-a-invalidade.md) (entrada inválida preservando a invalidade)

## Contexto

A chave de acesso de 44 posições identifica um documento fiscal eletrônico (NF-e, NFC-e, CT-e, CT-e OS, GTV-e,
MDF-e, BP-e, NF3e e NFCom) e carrega dado pessoal: o emitente pode ser uma pessoa física (produtor rural,
transportador autônomo, emissão avulsa), e a chave aparece em tabelas de notas, de eventos e de integração, muitas
vezes ao lado das colunas com o CPF ou o CNPJ do emitente. Trocá-la por um valor aleatório quebra as junções e as
restrições de unicidade; um hash truncado colide. A chave pseudonimizada precisa continuar válida (estrutura e dígito
verificador), bater com as colunas de CPF e CNPJ e nunca colidir.

O leiaute (posições contadas a partir de 1) é: cUF (1–2), AAMM (3–6), emitente (7–20), modelo (21–22), série
(23–25), número (26–34), tpEmis (35), código numérico (36–43; na NFCom e, desde a NT NF3e 2021.003, na NF3e, a
posição 36 é o `nSiteAutoriz` e o código tem 7 dígitos) e cDV (44, módulo 11 sobre as 43 primeiras posições, cada caractere valendo ASCII − 48).
Desde 07/2026 o CNPJ pode ser alfanumérico: letras maiúsculas nas posições 7 a 18.

O emitente pode ser CNPJ (14 posições) ou CPF (`000` e 11 dígitos), e a chave não diz qual. O dígito verificador não
decide: todo CPF que começa com `00` forma, com o prefixo `000`, um CNPJ de DV válido. As regras oficiais separam os
dois pela série (NF-e e NFC-e), pela data e, na Nota Fiscal Fácil (NFF), por uma posição do número; no CT-e e no
MDF-e nenhuma regra de série separa os dois com segurança, porque o Provedor de Assinatura e Autorização (PAA) usa as
mesmas séries e o mesmo tpEmis também para microempreendedor com CNPJ.

## Decisão

1. **Formas.** As 44 posições; o prefixo do atributo `Id` do modelo (`NFe` para 55 e 65; `CTe` para 57, 64 e 67;
   `MDFe` para 58; `BPe` para 63; `NF3e` para 66; `NFCom` para 62), comparado sem distinguir a caixa ASCII e
   devolvido como veio; ou 11 grupos de 4 separados por um espaço, sem prefixo. O preenchimento à direita de
   `char(n)` volta como veio. O pseudônimo depende só das 44 posições.
2. **Campos preservados:** cUF, AAMM, modelo, série, tpEmis e o `nSiteAutoriz`.
3. **Classificação do emitente, só por campos preservados ou por predicados que a saída mantém:**
   1. NF-e e NFC-e, na ordem: letra nas posições 7 a 18, ou AAMM anterior a 1808 (o emitente CPF na chave começou
      em 08/2018), dá CNPJ. tpEmis 3 com série fora de 970–989 e AAMM a partir de 2104 é a NFF: a posição 30 decide
      (1 CNPJ, 2 CPF, outro valor anomalia); tpEmis 3 com série fora de 970–989 e AAMM de 1808 a 2103 é anomalia.
      Depois, a série: 000–909 CNPJ; 910–969 CPF; na NF-e, 970–979 CPF e 980–989 CNPJ (PAA); o resto é anomalia.
      CPF sem `000` nas posições 7 a 9 é anomalia. Na NFC-e, o emitente CPF só é autorizado na NFF (rejeição 337):
      a faixa 910–969 fora dela só aparece em dado inválido, e a leitura como CPF continua segura.
   2. CT-e e MDF-e, pela regra dos zeros: letra nas posições 7 a 18 dá CNPJ; senão, `000` nas posições 7 a 9 dá CPF;
      senão, CNPJ, cuja saída exclui as raízes numéricas `000xxxxx` (com a ordem também permutada, os 12 caracteres
      numéricos menores que 10⁹), para não ser lida como CPF. A NFF é tpEmis 3 com série fora de 970–989 e AAMM a
      partir de 2006.
   3. CT-e OS, GTV-e, BP-e, NF3e e NFCom: sempre CNPJ (o leiaute só admite CNPJ no emitente).
   4. A chave de 44 zeros (valor de preenchimento de sistemas) volta como veio. Anomalia, forma errada, modelo fora
      dos nove ou dos aceitos pela coluna (inclusive a NFGás, modelo 76, e a NFAg, ainda fora do escopo), prefixo de
      outro modelo e a chave de 50 posições da NFS-e seguem a política de entrada malformada; a anomalia nunca cai em
      CNPJ por padrão.
4. **Emitente nos domínios das colunas.** O CPF vai ao pseudonimizador da coluna de CPF e o CNPJ ao da coluna de CNPJ
   (os mesmos domínios, versões e opções), para o pseudônimo bater com essas colunas. O DV interno errado é sempre
   mantido pelo deslocamento (δ mod 100) e contado; a política de DV da chave vale para o cDV.
5. **Número e código no domínio `dfe-numero`**, com a versão do transformador da chave, por permutação Feistel com
   o emitente pseudonimizado no ajuste:
   1. número inteiro em 10⁹ (o 0 fica como veio);
   2. na NFF, o dia permutado entre os dias que existem no mês do AAMM e no mês do número (24 rodadas, porque o
      domínio tem só 6 × 6 valores) e o sequencial em 10⁴ ou 10⁵ (o 0 fica), com o dia pseudonimizado no ajuste;
      mês e tipo do emissor ficam; data inválida deixa o dia como veio;
   3. código de 8 dígitos sem os 20 códigos proibidos pela regra da NF-e (os 10 repdígitos e as 10 sequências
      crescentes, estendidos a todos os modelos) e sem o número pseudonimizado (comparação numérica); código de 7
      dígitos sem exclusão. Um código de entrada excluído fica como veio e é contado.
6. **cDV** recalculado com o mesmo deslocamento da entrada (δ mod 10): válido continua válido, inválido continua
   inválido.
7. **Ordem:** emitente, depois número e código, depois o cDV. A inversa lê a classificação na saída, desfaz o código
   e o número (com o emitente ainda pseudonimizado no ajuste), depois o emitente, depois o cDV.
8. **Injetividade garantida.** A transformação é uma bijeção sobre as chaves classificadas:
   1. A classificação de uma saída é a da entrada: ela lê só campos preservados (cUF, AAMM, modelo, série, tpEmis e,
      na NFF, o mês e o tipo do emissor, que ficam; fora da NFF, nada do número) e predicados que a saída mantém. A
      presença de letra nas posições 7 a 18 se mantém nos dois modos do CNPJ (permutada a raiz, a partição é pela letra
      na raiz e a ordem fica literal; permutados os 12 caracteres, a partição é pela letra nos 12); o `000` do ramo CPF
      é escrito fixo; na regra dos zeros, a raiz `000xxxxx` é excluída da saída.
   2. Dentro de uma classe, cada parte é uma permutação do seu espaço condicionada ao que já está na saída (o emitente
      pela permutação da coluna; o número, as partes da NFF e o código pela permutação do ajuste formado por posições
      que a saída mostra; o cDV por uma translação em Z₁₀). Em cada parte, o conjunto que fica como veio é exatamente o
      conjunto excluído da saída, e ele é função só do ajuste (o número pseudonimizado no código; os dias dos dois
      meses no dia): o cycle-walking sobre o complemento, com a identidade sobre o conjunto, é uma bijeção do espaço
      todo. A inversa desfaz as partes na ordem contrária, com os mesmos ajustes.
   3. Todo valor copiado em claro (chave zerada, forma errada, modelo fora, prefixo de outro modelo, anomalia com a
      política de preservar) falha a forma, o modelo, o prefixo ou a classificação, e toda saída transformada passa
      nos quatro: as imagens não se cruzam.
   4. Ressalvas: com cDV errado e a política de anular, a saída é nula (a bijeção vale sobre as saídas não nulas); o
      cycle-walking tem um limite defensivo de 64 passos, que com os conjuntos excluídos daqui estoura com chance
      menor que 10⁻¹⁹⁰ e, se estourar, falha alto em vez de colidir.

## Alternativas consideradas

| Alternativa | Por que não |
| --- | --- |
| Valor aleatório ou hash truncado da chave inteira | Quebra junções com as colunas de CPF e CNPJ e colide (paradoxo do aniversário) em colunas de chave única |
| Decidir CPF ou CNPJ pelo DV do emitente | Todo CPF iniciado por `00` também é um CNPJ de DV válido com o prefixo `000`; a saída mudaria de classe e perderia a inversa |
| Separar CPF e CNPJ por série no CT-e e no MDF-e | Nenhuma regra de validação reserva série a CPF nesses modelos, e o PAA usa as mesmas séries com CNPJ; um CPF seria tratado como CNPJ e sairia com dígitos em claro |
| Tratar anomalia como CNPJ | Um CPF lido como CNPJ sairia com 4 dígitos em claro (a ordem é preservada) ou inteiro (raiz zero é ponto fixo) |
| Evitar o código igual ao número pseudonimizado por classes e involução | Vaza um bit do número e acrescenta 19 pontos fixos; o caso fica como veio, com chance da ordem de 10⁻⁸ por chave, e é contado |
| Aceitar prefixo de `Id` de outro modelo | No documento autorizado o prefixo sempre corresponde ao modelo; aceitar deixaria dado corrompido passar em silêncio |

## Consequências

- O emitente da chave bate com as colunas de CPF e CNPJ quando o DV está certo ou quando a coluna preserva o DV
  errado. Na regra dos zeros, cerca de 0,1% dos CNPJs de CT-e e de MDF-e (os que a permutação da coluna levaria a uma
  raiz `000xxxxx`) saem diferentes da coluna, e um CNPJ de raiz `000xxxxx` é lido como CPF.
- Nesse último caso, o deslocamento do DV interno, preservado, é uma função fixa do original: com a lista desses
  CNPJs (poucos, em geral entidades antigas e públicas), os candidatos se reduzem por cerca de 100. É um risco
  declarado.
- cUF, AAMM, modelo, série e tpEmis continuam em claro e são quase identificadores; a série da emissão por pessoa
  física pode identificar o estabelecimento.
- Na NF-e de série 890–899 (emissão avulsa pela SEFAZ), a chave traz o CNPJ da SEFAZ mesmo quando o emitente do
  documento é pessoa física: o emitente da chave não bate com a coluna de CPF do emissor.
- Na NFF, o mês e o tipo do emissor continuam em claro (o tipo diz se o emitente é CPF), o dia é permutado entre
  cerca de 30 valores e o sequencial entre 10⁴ ou 10⁵: proteção menor que a do número inteiro.
- Uma chave anômala com a política de preservar vai em claro; o relatório conta as anomalias à parte.
- Quem compõe o transformador precisa passar as mesmas instâncias das colunas de CPF e CNPJ; o construtor confere só
  o ambiente.
- Uma regra fiscal nova (série, PAA, NFF, modelo) muda a classificação: entra por versão nova do transformador.

## Evidência e verificação

- Fontes primárias: NT Conjunta 2025.001, Anexo II (cDV com letras, cada caractere valendo ASCII − 48), NT 2018.001
  (emitente CPF na chave e séries da emissão por pessoa física), NT 2013.007 (criação da SVC e desativação do SCAN),
  NT 2015.002 e NT 2021.002 (NFC-e com CPF só na NFF; NFF da NF-e e da NFC-e), NT 2019.001 (códigos numéricos
  proibidos), NT NF-e 2026.001 (PAA, Provedor de Assinatura e Autorização), NT CT-e 2020.001 e NT MDF-e 2020.002 (NFF
  de transporte), NT CT-e 2020.002 (GTV-e), NT CT-e 2024.002 e NT MDF-e 2024.002 (PAA estendido ao transportador
  autônomo, com tpEmis 3), MOC CT-e 4.00 (PAA vinculado ao microempreendedor), MOC MDF-e 3.00b, MOC NF3e e NT NF3e
  2021.003 (`nSiteAutoriz`); Perguntas e Respostas da Receita sobre o CNPJ alfanumérico.
- A garantia é o argumento da decisão 8; os testes o exercitam em cada ramo, por amostra. Teste de propriedade sobre a grade de células (modelo, série com as fronteiras, tpEmis, AAMM com as fronteiras das
  épocas, tipo de emitente e posição 30), com número, código e deslocamentos sorteados, duas chaves e os dois modos de
  permutar a ordem do CNPJ: classificação da saída igual à esperada por uma tabela escrita à parte, saída nunca
  anômala, estrutura, campos e deslocamentos preservados, emitente igual ao das colunas, inversa numa instância
  separada, nenhuma saída repetida entre entradas diferentes e P(P⁻¹(y)) = y. As caminhadas raras (regra dos zeros,
  repdígito do CPF, código proibido, código igual ao número pseudonimizado, número 0) são montadas por pré-imagem.
- As mesmas 14 posições lidas como CNPJ (série 001) e como CPF (série 920, PAA, NFF e NFC-e) voltam pela inversa e
  caem em domínios diferentes.

## Gatilho de revisão

Uma nota técnica que mude séries, o PAA, a NFF ou o leiaute da chave de qualquer dos nove modelos; a entrada de um
modelo novo com chave de 44 posições no escopo (a NFGás, modelo 76, e a NFAg já existem e ficam fora até lá); ou a
chave de 50 posições da NFS-e Padrão Nacional entrar no escopo.

## Gerador de chaves para dados de teste

O `GeradorBr.ChaveDfe(modelo, alfanumerico)` gera chaves válidas de emissão normal com emitente CNPJ de matriz, com o
AAMM numa janela fixa (de 01/2020 a 09/2026; com CNPJ alfanumérico, de 07/2026), porque a sequência por semente não
pode depender do relógio. A ordem dos sorteios, a tabela de UF e a janela fazem parte da sequência: mudá-las só numa
versão maior, com registro no CHANGELOG; dentro da versão maior, outra janela entra por sobrecarga nova.
