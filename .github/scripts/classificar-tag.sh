#!/usr/bin/env bash
# Classifica a tag do release. Só "lancamento" chega ao envio para o nuget.org e ao GitHub Release.
#   vX.Y.Z       -> lancamento (X >= 1: nada é publicado antes da 1.0.0)
#   vX.Y.Z-rc.N  -> ensaio (o release inteiro, sem envio; os pacotes ficam como artefato)
#   qualquer outra -> erro, e o workflow para.
set -euo pipefail

tag="${1:?informe a tag}"
numero='(0|[1-9][0-9]*)'

if [[ "$tag" =~ ^v${numero}\.${numero}\.${numero}$ ]]; then
  if [[ "${BASH_REMATCH[1]}" == "0" ]]; then
    echo "Tag de lançamento abaixo da 1.0.0: $tag" >&2
    exit 1
  fi
  echo lancamento
elif [[ "$tag" =~ ^v${numero}\.${numero}\.${numero}-rc\.${numero}$ ]]; then
  echo ensaio
else
  echo "Tag fora do padrão vX.Y.Z ou vX.Y.Z-rc.N: $tag" >&2
  exit 1
fi
