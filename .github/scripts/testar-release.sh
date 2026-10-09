#!/usr/bin/env bash
# Garante que só tag de lançamento (vX.Y.Z, X >= 1) chega ao envio para o nuget.org:
# 1. a classificação das tags;
# 2. a ligação no release.yml: só o job publicar envia e cria o GitHub Release, e só quando a tag é de lançamento.
# Requer o yq (mikefarah), que já vem no runner do Ubuntu.
set -euo pipefail

raiz="$(cd "$(dirname "$0")/../.." && pwd)"
classificar="$raiz/.github/scripts/classificar-tag.sh"
workflow="$raiz/.github/workflows/release.yml"
falhas=0

falhar() {
  echo "FALHOU: $1"
  falhas=$((falhas + 1))
}

esperar() {
  local tag="$1" esperado="$2" obtido
  obtido="$(bash "$classificar" "$tag" 2>/dev/null)" || obtido="erro"
  [[ "$obtido" == "$esperado" ]] || falhar "tag '$tag': esperado '$esperado', obtido '$obtido'"
}

esperar v1.0.0 lancamento
esperar v1.2.3 lancamento
esperar v10.20.30 lancamento
esperar v1.0.0-rc.0 ensaio
esperar v1.0.0-rc.12 ensaio
esperar v0.9.0-rc.1 ensaio
esperar v0.9.0 erro
esperar v1.0.0-beta.1 erro
esperar v1.0.0-rc erro
esperar v1.0.0-rc.01 erro
esperar v1.0.0-RC.1 erro
esperar v01.0.0 erro
esperar v1.0 erro
esperar 1.0.0 erro
esperar v1.0.0+build.1 erro
esperar "v1.0.0 " erro
esperar "" erro

valor() { yq -r "$1" "$workflow"; }

[[ "$(valor '.jobs.publicar.if')" == "needs.preparar.outputs.tipo == 'lancamento'" ]] \
  || falhar "o job publicar não está condicionado a tag de lançamento"
[[ "$(valor '.jobs.publicar.needs | contains(["preparar", "pacotes"])')" == "true" ]] \
  || falhar "o job publicar não depende de preparar e pacotes"
[[ "$(valor '.jobs.publicar.environment')" == "nuget" ]] \
  || falhar "o job publicar não usa o environment nuget"
[[ "$(valor '.jobs.validar-publicacao.if')" == "github.event_name == 'workflow_dispatch'" ]] \
  || falhar "o job validar-publicacao não é só do disparo manual"
[[ "$(valor '.jobs.validar-publicacao.environment')" == "nuget" ]] \
  || falhar "o job validar-publicacao não usa o environment nuget"
[[ "$(valor '.jobs.preparar.if')" == "github.event_name == 'push'" ]] \
  || falhar "o job preparar não é só do push de tag"
[[ "$(valor '[.jobs.preparar.steps[] | select(.run // "" | test("conferir-assinatura-tag.sh"))] | length')" == "1" ]] \
  || falhar "o job preparar não confere a assinatura da tag"
[[ "$(valor '.permissions | length')" == "0" ]] \
  || falhar "o workflow tem permissões fora dos jobs"

# Envio, Release e login só onde devem estar.
for job in $(valor '.jobs | keys | .[]'); do
  passos="$(yq -r ".jobs.\"$job\".steps[] | (.run // \"\") + \" \" + (.uses // \"\")" "$workflow")"
  if grep -q 'nuget push' <<<"$passos" && [[ "$job" != publicar ]]; then
    falhar "o job $job envia pacotes"
  fi
  if grep -q 'gh release' <<<"$passos" && [[ "$job" != publicar ]]; then
    falhar "o job $job cria GitHub Release"
  fi
  if grep -q 'NuGet/login' <<<"$passos" && [[ "$job" != publicar && "$job" != validar-publicacao ]]; then
    falhar "o job $job obtém credencial do nuget.org"
  fi
done
if [[ "$(yq -r '.jobs.validar-publicacao.steps[] | (.run // "") + " " + (.uses // "")' "$workflow" | grep -c -E 'nuget push|gh release')" != "0" ]]; then
  falhar "o job validar-publicacao faz mais que o login"
fi

if [[ "$falhas" -gt 0 ]]; then
  echo "$falhas verificação(ões) falharam."
  exit 1
fi
echo "Release: todas as verificações passaram."
