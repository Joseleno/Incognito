#!/usr/bin/env bash
# Recusa tag simples (não anotada) e tag sem assinatura verificada pelo GitHub.
# Uso: conferir-assinatura-tag.sh <dono/repositório> <tag>   (requer gh autenticado ou GH_TOKEN)
# Criar a tag: git tag -s vX.Y.Z -m "Incognito X.Y.Z", com a chave de assinatura cadastrada no GitHub.
set -euo pipefail

repositorio="${1:?informe o repositório}"
tag="${2:?informe a tag}"

tipo="$(gh api "repos/$repositorio/git/ref/tags/$tag" --jq .object.type)"
if [[ "$tipo" != tag ]]; then
  echo "A tag $tag não é anotada (aponta direto para um $tipo). Crie com: git tag -s $tag -m \"...\"" >&2
  exit 1
fi

objeto="$(gh api "repos/$repositorio/git/ref/tags/$tag" --jq .object.sha)"
verificado="$(gh api "repos/$repositorio/git/tags/$objeto" --jq .verification.verified)"
motivo="$(gh api "repos/$repositorio/git/tags/$objeto" --jq .verification.reason)"
if [[ "$verificado" != true ]]; then
  echo "A tag $tag não tem assinatura verificada pelo GitHub ($motivo)." >&2
  exit 1
fi
echo "Tag $tag anotada e com assinatura verificada."
