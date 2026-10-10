#!/usr/bin/env bash
# Garante que toda action usada nos workflows está fixada pelo SHA completo do commit, com a versão em
# comentário (o formato que o Dependabot mantém): uses: dono/action@<40 hexadecimais> # vX.Y.Z
# Uma tag (@v7) ou um branch (@main) pode ser movido para outro código sem que o repositório perceba.
set -euo pipefail
export LC_ALL=C  # [0-9a-f] só em minúsculas, independente da localidade

pasta="${1:-$(cd "$(dirname "$0")/../.." && pwd)/.github/workflows}"

usos="$(grep -rnE '^[[:space:]]*(-[[:space:]]+)?uses:' "$pasta" || true)"
if [[ -z "$usos" ]]; then
  echo "Nenhum 'uses:' encontrado em $pasta" >&2
  exit 1
fi

fora="$(grep -vE 'uses:[[:space:]]*[^@[:space:]]+@[0-9a-f]{40}[[:space:]]+#[[:space:]]*v[0-9]' <<< "$usos" || true)"
if [[ -n "$fora" ]]; then
  echo "Actions sem SHA completo e comentário de versão:" >&2
  echo "$fora" >&2
  exit 1
fi

echo "$(wc -l <<< "$usos") usos de actions, todos fixados por SHA."
