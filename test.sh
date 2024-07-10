#!/usr/bin/env bash
set -euo pipefail

script_dir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
new_line=$(printf '\n')

dotnet build

cd "$script_dir/fixture" || exit 1
IFS=$new_line; mapfile -t tracked_files < <(git ls-files)
tracked_files_concat=$(IFS=':'; printf '%s' "${tracked_files[*]}")

find ./ -name "obj" -type d -exec rm -rv '{}' ';' || true
find ./ -name "bin" -type d -exec rm -rv '{}' ';' || true

rm "$1/$1.nix" || true
TRACKED_FILES="$tracked_files_concat" dotnet build "$1/$1.csproj" \
  --no-incremental \
  --nologo \
  --configuration:Release \
  --property:Deterministic=true \
  --property:RunAnalyzers=false \
  "-logger:$script_dir/src/Logger/bin/Debug/net8.0/Logger.dll"

echo "{}" > "$1/deps.nix"
generate_deps=$(nix build "$script_dir#$2.passthru.fetch-deps" --no-link --print-out-paths)
# echo "$generate_deps"
"$generate_deps" "$1/deps.nix"

echo "Done"
