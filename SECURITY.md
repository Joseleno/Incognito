# Política de segurança

## Versões com suporte

Nenhuma versão foi publicada ainda. A partir da `1.0.0`, correções de segurança saem apenas na última versão publicada.

## Como relatar uma vulnerabilidade

**Não** abra issue, discussão ou pull request público para problemas de segurança.

Relate em privado pelo [relato privado de vulnerabilidades do GitHub](https://github.com/Joseleno/Incognito/security/advisories/new), descrevendo o que encontrou, como reproduzir e o impacto esperado.

**Nunca inclua no relato dados pessoais reais, chaves do Incognito ou strings de conexão.** Reproduza com dados sintéticos; se o problema depende de um valor específico, descreva o formato dele em vez de colar o valor.

O que esperar:

- O projeto é mantido por uma pessoa, em regime de melhor esforço e sem prazo garantido.
- A meta é uma primeira resposta em até 72 horas e, se a falha se confirmar, um plano de correção em até 14 dias.
- Crédito no aviso de segurança e no `CHANGELOG.md`, se você quiser.

## Escopo

Dentro do escopo:

- Qualquer saída da ferramenta (stdout, stderr, log, relatório ou exceção) que contenha o valor de uma coluna tratada ou a chave.
- Recuperar o valor original a partir do valor gerado sem conhecer a chave, ou deduzir a chave a partir das saídas.
- Valor de coluna classificada que chega ao destino sem transformação, fora dos casos em que a configuração pede para preservá-lo.
- Escrita em um banco diferente do destino preparado, contornando a proteção do destino.
- Injeção de SQL por nomes de esquema, tabela ou coluna, ou pelo arquivo de configuração.
- Chamada de rede além dos bancos configurados e da verificação de versão opcional.
- As classes usuais de vulnerabilidade em bibliotecas, como desserialização insegura ou dependência vulnerável.

Fora do escopo:

- Coluna com dado pessoal que não foi classificada na configuração, ou que foi marcada para ser copiada sem transformação.
- Reidentificação por cruzamento da base gerada com outras fontes. O modo `efemero` não é anonimização garantida e o modo `reproduzivel` gera dado pessoal; quem tem a chave do modo `reproduzivel` reverte todos os valores.
- Vazamento da chave do modo `reproduzivel` por quem a guarda.
- Logs do próprio servidor de banco de dados, que ficam fora do controle da ferramenta.
