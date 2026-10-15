using System.Diagnostics.CodeAnalysis;

namespace CodeProcess.Incognito.Normas;

/// <summary>
/// Os dados do catálogo normativo: só a propriedade de dados e campos de data, sem lógica (testes conferem a forma da
/// classe e o que o inicializador cita).
/// </summary>
/// <remarks>
/// Para rever uma referência: abrir a fonte primária, atualizar a observação e a data da revisão, e dizer na observação
/// o que foi visto só em fonte secundária. Os dados ficam fora da mutação noturna (num inicializador estático, cada
/// cadeia mutada roda a suíte inteira até esgotar o tempo) e da cobertura, que instrumentaria o inicializador.
/// </remarks>
// Stryker disable once all : dado conferido por revisão humana e pelos testes de invariantes, não lógica
[ExcludeFromCodeCoverage(Justification = "Só dados, conferidos pelos testes de invariantes.")]
internal static class ReferenciasNormativas
{
    private static readonly DateOnly _revisaoDe20261015 = new(2026, 10, 15);

    /// <summary>As referências, as jurídicas primeiro e depois as técnicas que fixam a saída dos transformadores.</summary>
    internal static IReadOnlyList<ReferenciaNormativa> Todas { get; } = Array.AsReadOnly<ReferenciaNormativa>(
    [
        new(
            identificador: "lgpd",
            titulo: "Lei nº 13.709, de 14 de agosto de 2018 (Lei Geral de Proteção de Dados Pessoais)",
            emissor: "Congresso Nacional",
            dispositivos: "arts. 5º, 6º, 11, 12, 13, 14, 46, 48 e 49",
            situacao: SituacaoNormativa.Atual,
            observacao: "Texto compilado com as Leis nº 13.853/2019, 14.010/2020, 14.460/2022 e 15.352/2026; a Lei nº "
                + "15.352/2026 (conversão da Medida Provisória nº 1.317/2025) dá nova redação ao art. 55-A, que cria a "
                + "Agência Nacional de Proteção de Dados (ANPD), sucessora da Autoridade (art. 15, parágrafo único).",
            motivo: "Referência considerada na documentação dos modos efemero e reproduzivel e dos riscos declarados.",
            fonte: new("https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "anpd-estudo-anonimizacao-analise-juridica",
            titulo: "Estudo Técnico sobre a Anonimização de Dados na LGPD: Análise Jurídica, versão 1.0, novembro de 2023",
            emissor: "Autoridade Nacional de Proteção de Dados (ANPD)",
            dispositivos: null,
            situacao: SituacaoNormativa.Atual,
            observacao: "Documento orientativo, sem força de norma.",
            motivo: "Referência considerada no texto sobre anonimização, pseudonimização (art. 13, § 4º) e risco de "
                + "reidentificação.",
            fonte: new("https://www.gov.br/anpd/pt-br/centrais-de-conteudo/documentos-tecnicos-orientativos"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "anpd-estudo-anonimizacao-tecnicas",
            titulo: "Estudo Técnico sobre Anonimização de Dados na LGPD: Uma Visão de Processo Baseado em Risco e "
                + "Técnicas Computacionais, versão 1.0, novembro de 2023",
            emissor: "Autoridade Nacional de Proteção de Dados (ANPD)",
            dispositivos: null,
            situacao: SituacaoNormativa.Atual,
            observacao: "Documento orientativo, sem força de norma; base do Estudo Preliminar.",
            motivo: "Referência considerada no texto sobre risco de reidentificação e técnicas de anonimização (o estudo "
                + "não trata de pseudonimização).",
            fonte: new("https://www.gov.br/anpd/pt-br/centrais-de-conteudo/documentos-tecnicos-orientativos"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "anpd-guia-anonimizacao",
            titulo: "Guia de Anonimização e Pseudonimização (em elaboração): Estudo Preliminar de Anonimização e "
                + "Pseudonimização para a Proteção de Dados Pessoais, versão 1.0, dezembro de 2023, Tomada de Subsídios nº 1/2024",
            emissor: "Autoridade Nacional de Proteção de Dados (ANPD)",
            dispositivos: null,
            situacao: SituacaoNormativa.EmElaboracao,
            observacao: "Consulta de 30/01/2024 a 14/03/2024, após prorrogação; guia final não publicado na revisão "
                + "(item 9 da Agenda Regulatória 2025-2026, com nova minuta remetida ao Conselho Diretor segundo o balanço "
                + "do 1º semestre de 2026; em fase final de aprovação segundo a Nota Técnica nº 20/2026 da ANPD, de "
                + "31/08/2026, item 5.5.5.5).",
            motivo: "Referência considerada na distinção entre pseudonimização e anonimização.",
            fonte: new("https://www.gov.br/anpd/pt-br/acesso-a-informacao/participacao-social/outras-acoes"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "iso-iec-27001-2022",
            titulo: "ISO/IEC 27001:2022, Information security, cybersecurity and privacy protection: Information "
                + "security management systems: Requirements, com a emenda 1:2024",
            emissor: "ISO e IEC",
            dispositivos: "Anexo A, controles 8.11 (Data masking), 8.31 (Separation of development, test and production "
                + "environments) e 8.33 (Test information)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Edição de 25/10/2022 e emenda 1:2024 conferidas no catálogo da ISO; títulos dos controles "
                + "confirmados em fonte secundária (o texto da norma é pago). Há a ABNT NBR ISO/IEC 27001:2022.",
            motivo: "Referência considerada no texto sobre mascaramento, separação de ambientes e informação de teste.",
            fonte: new("https://www.iso.org/standard/27001"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "tjue-c-413-23-p",
            titulo: "Tribunal de Justiça da União Europeia, acórdão de 4 de setembro de 2025, processo C-413/23 P "
                + "(Autoridade Europeia para a Proteção de Dados contra Conselho Único de Resolução)",
            emissor: "Tribunal de Justiça da União Europeia",
            dispositivos: null,
            situacao: SituacaoNormativa.Atual,
            observacao: "Referência comparada, sem efeito no direito brasileiro; trata do Regulamento (UE) 2018/1725. "
                + "Data, partes e regulamento conferidos no comunicado de imprensa nº 107/25 (a fonte); Secção e ECLI "
                + "(ECLI:EU:C:2025:645) só em fonte secundária.",
            motivo: "Referência comparada: dado pseudonimizado pode não ser pessoal para quem não tem meios de "
                + "reidentificar, mas o dever de informação se avalia do ponto de vista do responsável, na coleta.",
            fonte: new("https://curia.europa.eu/site/upload/docs/application/pdf/2025-09/cp250107en.pdf"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-conjunta-2025-001",
            titulo: "Nota Técnica Conjunta 2025.001: CNPJ Alfanumérico, versão 1.00, de 25 de abril de 2025",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "chave de acesso: formação e dígito verificador",
            situacao: SituacaoNormativa.Atual,
            observacao: "Produção a partir de 06/07/2026 pelo cronograma da nota; os schemas da NF-e vêm da Nota Técnica "
                + "2026.004, versão 1.01 (produção em 01/07/2026).",
            motivo: "Chave de acesso com CNPJ alfanumérico no ValidadorBr, no GeradorBr e no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=5ZkvIZt10mQ="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "in-rfb-2229-2024",
            titulo: "Instrução Normativa RFB nº 2.229, de 15 de outubro de 2024",
            emissor: "Secretaria Especial da Receita Federal do Brasil",
            dispositivos: "CNPJ alfanumérico: formação e dígitos verificadores",
            situacao: SituacaoNormativa.Atual,
            observacao: "Pelo cronograma da Receita Federal (a fonte), sistemas em produção em 27/07/2026 e primeira "
                + "inscrição alfanumérica em 31/07/2026 (a emissão nessa data, só em fonte secundária). O texto da "
                + "instrução, que altera a Instrução Normativa RFB nº 2.119/2022, foi confirmado em fonte secundária.",
            motivo: "CNPJ alfanumérico no ValidadorBr, no GeradorBr e no br.cnpj.",
            fonte: new("https://www.gov.br/receitafederal/pt-br/acesso-a-informacao/acoes-e-programas/programas-e-atividades/cnpj-alfanumerico"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2021-002",
            titulo: "Nota Técnica 2021.002: Adequações para a Nota Fiscal Fácil (NFF), versão 1.12",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "item 2.1 (número da NF-e emitida pela NFF)",
            situacao: SituacaoNormativa.Atual,
            observacao: "A capa data a versão 1.12 de janeiro de 2025 e o controle de versões, de janeiro de 2024; o "
                + "portal a publicou em 28/01/2025.",
            motivo: "Tipo do emitente e dia da NFF no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=hFtSfuCjY0A="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-cte-2020-001",
            titulo: "Nota Técnica 2020.001 do CT-e: Regime Especial da Nota Fiscal Fácil (NFF), versão 1.05, julho de 2021",
            emissor: "Portal Nacional do CT-e",
            dispositivos: "número do CT-e emitido pela NFF",
            situacao: SituacaoNormativa.Atual,
            observacao: "Título, versão e data conferidos na listagem do portal; o documento não tem texto extraível, e o "
                + "leiaute do número (mês, dia e sequencial de 5 dígitos) foi confirmado em fonte secundária.",
            motivo: "Dia da NFF do transporte no br.chave-dfe.",
            fonte: new("https://www.cte.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=UlPhj6KhFNg="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-mdfe-2020-002",
            titulo: "Nota Técnica 2020.002 do MDF-e: Nota Fiscal Fácil (NFF), versão 1.05, julho de 2021",
            emissor: "Portal Nacional do MDF-e",
            dispositivos: "item 2 (número do MDF-e emitido pela NFF)",
            situacao: SituacaoNormativa.Atual,
            observacao: "O cabeçalho da tabela ainda cita o leiaute anterior (dia, PV e número), que MDF-e anterior à "
                + "versão 1.03 pode usar.",
            motivo: "Dia da NFF do transporte no br.chave-dfe.",
            fonte: new("https://dfe-portal.svrs.rs.gov.br/MDFE/DownloadArquivoEstatico/?sistema=MDFE&tipoArquivo=3&nomeArquivo=MDFe_Nota_Tecnica_2020_002_NFF_1.05.pdf"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2019-001",
            titulo: "Nota Técnica 2019.001: Criação e Atualização de Regras de Validação, versão 1.70, agosto de 2025",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "regra B03-10 (rejeição 897: códigos numéricos proibidos e código igual ao número)",
            situacao: SituacaoNormativa.Atual,
            observacao: "O histórico retira o modelo 65 da regra, mas a tabela ainda cita 55 e 65; o projeto exclui os "
                + "códigos em todos os modelos.",
            motivo: "Códigos numéricos excluídos da saída no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=suszdTDFNOk="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2026-001",
            titulo: "Nota Técnica 2026.001: Provedor de Assinatura e Autorização de DF-e (PAA), versão 1.02b, julho de 2026",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "campo B07 e regra B07-10 (séries do PAA)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Séries 970 a 979 para CPF e 980 a 989 para CNPJ, do PAA, só no modelo 55 (a regra B07-10 as "
                + "recusa no 65).",
            motivo: "Classificação do emitente pela série no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=AlgHfV6gpAU="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2023-002",
            titulo: "Nota Técnica 2023.002: Emitente Pessoa Física (CPF) com Inscrição Estadual na NFC-e, versão 1.01, "
                + "novembro de 2025",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "itens 2.1 e 4.1 e regra C02a-10 (séries do emitente pessoa física)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Séries 920 a 969 para o emitente pessoa física com aplicativo próprio na NFC-e; a versão 1.01 "
                + "admite a NFC-e pela nota avulsa em SC, e a regra C02a-10 aceita CPF nas séries 890 a 899 e 910 a 969, "
                + "nos modelos 55 e 65.",
            motivo: "Classificação do emitente pela série no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=T3QyeMfpios="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2018-001",
            titulo: "Nota Técnica 2018.001: Emitente Pessoa Física (CPF) com Inscrição Estadual, versão 1.10, fevereiro de "
                + "2020",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "itens 1, 2.1, 3.1 e 3.2 (CPF na chave e faixas de série) e regras do grupo C (série 000 a 909 "
                + "para CNPJ; 890 a 899 e 910 a 969 para CPF)",
            situacao: SituacaoNormativa.Atual,
            observacao: "A capa data a versão 1.10 de fevereiro de 2020 e o portal a publicou em 27/02/2020; a versão 1.00 é "
                + "de abril de 2018. O CPF entra na chave com zeros à esquerda, e a série marca o emitente: homologação em "
                + "01/08/2018 e produção em 01/10/2018 (item 1); o projeto toma 08/2018 como início para cobrir também as "
                + "chaves de homologação. Nas séries 890 a 899 (nota avulsa no site do fisco), a chave traz o CNPJ da SEFAZ.",
            motivo: "Início do emitente CPF na chave (AAMM 1808) e faixas de série do emitente no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=JlPBgTc3TQo="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "nt-nfe-2013-007",
            titulo: "Nota Técnica 2013.007: SEFAZ Virtual de Contingência (SVC), versão 1.03, agosto de 2014",
            emissor: "Portal Nacional da NF-e",
            dispositivos: "itens 01.1 (prazos e desativação do SCAN), 05.2 (tpEmis na chave no leiaute 2.0) e 05.5 "
                + "(séries 900 a 999 do SCAN)",
            situacao: SituacaoNormativa.Atual,
            observacao: "A capa data a versão 1.03 de agosto de 2014 e o portal a publicou em 16/09/2014. O prazo previsto "
                + "para desativar o SCAN (tpEmis 3) era 30/09/2014; a data efetiva não foi conferida, mas a Nota Técnica "
                + "2015.002, versão 1.41 (agosto de 2016), e a Nota Técnica 2018.001 já marcam o SCAN como desativado.",
            motivo: "tpEmis 3 da NF-e e da NFC-e antes da NFF tratado como anomalia no br.chave-dfe.",
            fonte: new("https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=b4h3Pi+Helo="),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "res-anatel-749-2022",
            titulo: "Resolução Anatel nº 749, de 15 de março de 2022 (Regulamento de Numeração dos Serviços de "
                + "Telecomunicações)",
            emissor: "Agência Nacional de Telecomunicações (Anatel)",
            dispositivos: "Regulamento anexo, arts. 11, 12, 15 e 18 (códigos de acesso de 8 e 9 dígitos, código nacional "
                + "e códigos não geográficos)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Texto atualizado na página da Anatel em 05/01/2026; os arts. 12, 15 e 18 sem alteração; o inciso "
                + "II do art. 11 foi revogado pela Resolução nº 768/2024.",
            motivo: "Classes de número e números de serviço preservados no br.telefone.",
            fonte: new("https://informacoes.anatel.gov.br/legislacao/resolucoes/2022/1641-resolucao-749"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "anatel-pgcn",
            titulo: "Plano Geral de Códigos Nacionais (PGCN), republicado pelo Despacho Decisório nº 12/2022/PRRE/SPR",
            emissor: "Agência Nacional de Telecomunicações (Anatel)",
            dispositivos: "códigos nacionais (DDD)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Os 67 códigos (5.570 municípios) conferidos no anexo publicado com a Resolução nº 749/2022, "
                + "revogado pela Resolução nº 755/2022; o texto republicado pelo despacho não foi aberto, só a menção na "
                + "página da resolução.",
            motivo: "DDDs no br.telefone e no GeradorBr.",
            fonte: new("https://informacoes.anatel.gov.br/legislacao/resolucoes/2022/1641-resolucao-749"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "ato-anatel-12712-2024",
            titulo: "Ato Anatel nº 12.712, de 4 de setembro de 2024 (Procedimento para a Atribuição e Designação de "
                + "Recursos de Numeração)",
            emissor: "Agência Nacional de Telecomunicações (Anatel)",
            dispositivos: "Anexo, itens 7.1 a 7.4 (reserva dos códigos não geográficos, com a razão social e o CNPJ do "
                + "assinante no nSAPN)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Texto na página da Anatel atualizada em 07/05/2026, marcada como vigente. Vigência do "
                + "procedimento: 03/12/2024, quando revogou o Ato nº 13.672/2022; o item 9 (código 303) tem a redação do "
                + "Acórdão nº 201/2025. O item 7.4 pede o CNPJ do assinante, sem dizer com essas palavras que pessoa física "
                + "fica de fora. A suspensão do ato por acórdão de 23/12/2024 só aparece em fonte secundária, sem o alcance; "
                + "a página não a registra.",
            motivo: "Números de serviço (0800, 0300, 0303, 0500 e 0900) preservados no br.telefone, por não serem de "
                + "pessoa física.",
            fonte: new("https://informacoes.anatel.gov.br/legislacao/atos-de-numeracao/2024/2140-ato-12712"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "res-anatel-709-2019",
            titulo: "Resolução Anatel nº 709, de 27 de março de 2019 (Regulamento Geral de Numeração)",
            emissor: "Agência Nacional de Telecomunicações (Anatel)",
            dispositivos: "art. 2º da resolução (migração dos códigos não geográficos para 7 dígitos depois da série), "
                + "revogado pela Resolução nº 769/2024",
            situacao: SituacaoNormativa.Atual,
            observacao: "Referência histórica: o art. 2º mandava migrar em 18 meses os códigos não geográficos para o "
                + "formato do art. 44 da Resolução nº 86/1998 (série de 3 dígitos e mais 7), revogada pela Resolução nº "
                + "749/2022, que traz o formato atual no art. 18. Texto atualizado na página da Anatel em 17/11/2025; o "
                + "regulamento segue vigente, sem o capítulo de definições (Resolução nº 779/2025). Os 6 dígitos dos números "
                + "antigos e o prazo de 28/09/2020 só em fonte secundária.",
            motivo: "Números de serviço no formato anterior à migração (6 dígitos depois da série) preservados no "
                + "br.telefone.",
            fonte: new("https://informacoes.anatel.gov.br/legislacao/resolucoes/2019/1261-resolucao-709"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "correios-faixas-cep",
            titulo: "Faixas de CEP por unidade da federação",
            emissor: "Empresa Brasileira de Correios e Telégrafos (Correios)",
            dispositivos: null,
            situacao: SituacaoNormativa.Atual,
            observacao: "Confirmadas em fonte secundária (a consulta dos Correios exige verificação humana). A faixa antiga "
                + "de RO é aceita só como entrada.",
            motivo: "Faixas de saída e UF da entrada no br.cep e no GeradorBr.",
            fonte: new("https://buscacepinter.correios.com.br/app/faixa_cep_uf_localidade/index.php"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "ibge-dtb-2025",
            titulo: "Divisão Territorial Brasileira (DTB) 2025, data base de 31 de dezembro de 2025",
            emissor: "Instituto Brasileiro de Geografia e Estatística (IBGE)",
            dispositivos: "códigos das unidades da federação (2 dígitos)",
            situacao: SituacaoNormativa.Atual,
            observacao: "Os 27 códigos de UF conferidos no relatório de municípios do arquivo da DTB 2025 e na API de "
                + "localidades do IBGE; a página de códigos dos municípios do IBGE recusou a leitura automática. O campo cUF "
                + "da NF-e usa a tabela do IBGE (MOC 7.0, Anexo I, campo B02).",
            motivo: "cUF das chaves de DF-e geradas pelo GeradorBr.",
            fonte: new("https://geoftp.ibge.gov.br/organizacao_do_territorio/estrutura_territorial/divisao_territorial/2025/DTB_2025.zip"),
            revisadaEm: _revisaoDe20261015),
        new(
            identificador: "bcb-manual-operacional-dict",
            titulo: "Manual Operacional do Diretório de Identificadores de Contas Transacionais (DICT), versão 8.5",
            emissor: "Banco Central do Brasil",
            dispositivos: "seção 1 (formatos das chaves Pix e chave aleatória)",
            situacao: SituacaoNormativa.Atual,
            observacao: "A chave aleatória é um UUID gerado pelo DICT no formato da RFC 4122, e o CNPJ tem 14 caracteres "
                + "alfanuméricos desde a versão 8.4 (histórico com datas de 01/07/2026 para a 8.4 e de 01/09/2026 e "
                + "26/10/2026 para a 8.5). O nome EVP e as expressões regulares (UUID só em minúsculas, CNPJ sem distinção "
                + "de caixa, telefone com o sinal de mais) vêm da especificação da API do DICT, versão 2.12.1, no mesmo sítio.",
            motivo: "Formas das chaves Pix no br.chave-pix e no GeradorBr.",
            fonte: new("https://www.bcb.gov.br/content/estabilidadefinanceira/pix/Regulamento_Pix/X_ManualOperacionaldoDICT.pdf"),
            revisadaEm: _revisaoDe20261015),
    ]);
}
