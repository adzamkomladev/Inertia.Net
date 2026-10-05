#!/usr/bin/env bash
# Publishes tests/Inertia.Net.AotSmoke as a Native AOT binary and runs the protocol smoke checks against it.
# Any trim/AOT warning (IL2xxx/IL3xxx) fails the publish. Needs the .NET 10 SDK plus clang and zlib (Linux: apt install clang zlib1g-dev).
# Usage: eng/aot-smoke.sh            Env: RID (default linux-x64), PORT (default 5199).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
RID=${RID:-linux-x64}
OUT="$ROOT/artifacts/aot-smoke"
rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$ROOT/tests/Inertia.Net.AotSmoke/Inertia.Net.AotSmoke.csproj" -c Release -r "$RID" \
  -p:PublishAot=true -p:TreatWarningsAsErrors=true -o "$OUT/bin" 2>&1 | tee "$OUT/publish.log"

# Belt and braces: TreatWarningsAsErrors already fails the publish, but make sure no trim/AOT warning slipped through.
if grep -E "(warning|error) IL[23][0-9]{3}" "$OUT/publish.log"; then
  echo "Trim/AOT warnings found (see above)."
  exit 1
fi

bash "$ROOT/tests/Inertia.Net.AotSmoke/smoke.sh" "$OUT/bin/Inertia.Net.AotSmoke"
