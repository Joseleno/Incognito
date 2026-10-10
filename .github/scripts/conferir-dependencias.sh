#!/usr/bin/env bash
# Garante que o dotnet restore --locked-mode do CI cobre todo binário que entra no build:
# 1. todo projeto versionado tem packages.lock.json versionado ao lado: sem ele, o locked-mode gera um lock
#    novo e não trava nada;
# 2. nenhum arquivo do MSBuild muda onde fica o lock, desliga a trava, sobrepõe a versão central, baixa pacote
#    fora do grafo, referencia DLL ou analisador por caminho, ou executa código próprio no build.
# Única exceção: o RestorePackagesWithLockFile do Directory.Build.props da raiz, que liga o lock.
set -euo pipefail
export LC_ALL=C

cd "${1:-$(cd "$(dirname "$0")/../.." && pwd)}"
falhas=0

falhar() {
  echo "FALHOU: $1" >&2
  falhas=$((falhas + 1))
}

projetos="$(git ls-files '*.csproj' '*.fsproj' '*.vbproj')"
[[ -n "$projetos" ]] || falhar "nenhum projeto versionado"
while IFS= read -r projeto; do
  lock="$(dirname "$projeto")/packages.lock.json"
  git ls-files --error-unmatch "$lock" > /dev/null 2>&1 || falhar "$projeto sem $lock versionado"
done <<< "$projetos"

proibidas='NuGetLockFilePath|RestorePackagesWithLockFile|RestoreLockedMode|RestoreForceEvaluate|VersionOverride|PackageDownload|HintPath|<Reference[[:space:]>]|<Analyzer[[:space:]>]|UsingTask|DownloadFile|<Exec[[:space:]>]'
achados="$(git grep -nIiE "$proibidas" -- '*.csproj' '*.fsproj' '*.vbproj' '*.proj' '*.props' '*.targets' '*.rsp' \
  | grep -vE '^Directory\.Build\.props:[0-9]+:[[:space:]]*<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>[[:space:]]*$' || true)"
if [[ -n "$achados" ]]; then
  falhar "configuração do MSBuild que escapa do lock:"
  echo "$achados" >&2
fi

if [[ "$falhas" -gt 0 ]]; then
  exit 1
fi
echo "$(wc -l <<< "$projetos") projetos com lock versionado e nenhuma configuração que escape do lock."
