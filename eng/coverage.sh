#!/usr/bin/env bash
# Line coverage of the library assemblies over every test project (net10.0), merged, with the least-covered files.
# Fails when it is below MIN (default 0). Run after: dotnet build Inertia.Net.slnx -c Release
# Usage: eng/coverage.sh    Env: MIN (percent), TOP (files to list, default 20).
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/artifacts/coverage"
rm -rf "$OUT"
mkdir -p "$OUT"

for project in "$ROOT"/tests/*Tests; do
  name=$(basename "$project")
  dotnet test --project "$project" -c Release --no-build -f net10.0 -- \
    --coverage --coverage-output-format cobertura --coverage-settings "$ROOT/eng/coverage.xml" \
    --coverage-output "$OUT/$name.cobertura.xml" --results-directory "$OUT/$name" >"$OUT/$name.log" 2>&1 \
    || { echo "$name failed:"; tail -30 "$OUT/$name.log"; exit 1; }
done

python3 - "$OUT" "${MIN:-0}" "${TOP:-20}" <<'PY'
import glob, os, sys, xml.etree.ElementTree as ET
out, minimum, top = sys.argv[1], float(sys.argv[2]), int(sys.argv[3])
hits = {}  # (file, line) -> covered, merged over the test projects
for path in glob.glob(os.path.join(out, "**", "*.cobertura.xml"), recursive=True):
    for cls in ET.parse(path).iter("class"):
        name = cls.get("filename").replace("\\", "/")
        if "/obj/" in name or "/src/" not in name: continue  # generated code, or not the libraries
        name = name[name.index("/src/") + 1:]
        for line in cls.iter("line"):
            key = (name, int(line.get("number")))
            hits[key] = hits.get(key, False) or int(line.get("hits")) > 0
files = {}
for (name, _), covered in hits.items():
    total, done = files.get(name, (0, 0))
    files[name] = (total + 1, done + covered)
total = sum(t for t, _ in files.values())
done = sum(d for _, d in files.values())
rate = 100 * done / total if total else 0
print(f"Least-covered files (of {len(files)}):")
for name, (t, d) in sorted(files.items(), key=lambda f: (f[1][1] / f[1][0], -f[1][0]))[:top]:
    print(f"  {100 * d / t:5.1f}%  {d:5}/{t:<5} {name}")
print(f"Line coverage: {rate:.2f}% ({done}/{total} lines); minimum {minimum}%")
sys.exit(1 if rate < minimum else 0)
PY
