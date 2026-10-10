#!/usr/bin/env bash
# Recusa tag simples (não anotada), tag cujo nome interno difere do nome da ref e tag que não foi assinada
# por uma chave SSH de assinatura do dono do repositório. "Verificada pelo GitHub" sozinho não basta: vale
# para qualquer pessoa com chave cadastrada no GitHub.
# Uso: conferir-assinatura-tag.sh <dono/repositório> <tag>   (requer gh autenticado ou GH_TOKEN, e ssh-keygen)
# Criar a tag: git tag -s vX.Y.Z -m "Incognito X.Y.Z", com a chave SSH de assinatura cadastrada no GitHub.
set -euo pipefail

repositorio="${1:?informe o repositório}"
tag="${2:?informe a tag}"
dono="${repositorio%%/*}"

tipo="$(gh api "repos/$repositorio/git/ref/tags/$tag" --jq .object.type)"
if [[ "$tipo" != tag ]]; then
  echo "A tag $tag não é anotada (aponta direto para um $tipo). Crie com: git tag -s $tag -m \"...\"" >&2
  exit 1
fi

objeto="$(gh api "repos/$repositorio/git/ref/tags/$tag" --jq .object.sha)"
nome="$(gh api "repos/$repositorio/git/tags/$objeto" --jq .tag)"
if [[ "$nome" != "$tag" ]]; then
  echo "A ref $tag aponta para o objeto da tag $nome." >&2
  exit 1
fi

verificado="$(gh api "repos/$repositorio/git/tags/$objeto" --jq .verification.verified)"
motivo="$(gh api "repos/$repositorio/git/tags/$objeto" --jq .verification.reason)"
if [[ "$verificado" != true ]]; then
  echo "A tag $tag não tem assinatura verificada pelo GitHub ($motivo)." >&2
  exit 1
fi

# --template grava os bytes exatos, sem a quebra de linha que o --jq acrescenta.
temporario="$(mktemp -d)"
trap 'rm -rf "$temporario"' EXIT
gh api "repos/$repositorio/git/tags/$objeto" --template '{{.verification.payload}}' > "$temporario/conteudo"
gh api "repos/$repositorio/git/tags/$objeto" --template '{{.verification.signature}}' > "$temporario/assinatura"
gh api "users/$dono/ssh_signing_keys" --jq ".[] | \"$dono \" + .key" > "$temporario/permitidas"
if [[ ! -s "$temporario/permitidas" ]]; then
  echo "$dono não tem chave SSH de assinatura cadastrada no GitHub." >&2
  exit 1
fi
if ! ssh-keygen -Y verify -f "$temporario/permitidas" -I "$dono" -n git -s "$temporario/assinatura" \
  < "$temporario/conteudo" > /dev/null 2>&1; then
  echo "A tag $tag não foi assinada por uma chave SSH de assinatura de $dono." >&2
  exit 1
fi
echo "Tag $tag anotada e assinada por $dono."
