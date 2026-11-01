#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if command -v dotnet >/dev/null 2>&1; then
    azoth_dotnet="$(command -v dotnet)"
elif [[ -n "${DOTNET_ROOT:-}" && -x "$DOTNET_ROOT/dotnet" ]]; then
    azoth_dotnet="$DOTNET_ROOT/dotnet"
else
    echo 'Install the .NET 10 SDK, then build AZOTH.slnx in Release configuration.' >&2
    exit 1
fi

application_dir="$repo_dir/src/AZOTH.App/bin/Release/net10.0"
if [[ ! -f "$application_dir/AZOTH.dll" ]]; then
    echo 'Build first: dotnet build AZOTH.slnx -c Release' >&2
    exit 1
fi

# A review copy uses separate data and leaves desktop integration alone.
export AZOTH_DATA_DIR="${AZOTH_DATA_DIR:-${TMPDIR:-/tmp}/azoth-review-${UID}}"
if [[ -x "$application_dir/AZOTH" ]]; then
    exec "$application_dir/AZOTH" "$@"
fi
exec "$azoth_dotnet" "$application_dir/AZOTH.dll" "$@"
