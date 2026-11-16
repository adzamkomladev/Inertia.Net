#!/usr/bin/env bash
# Merges the reports of a split run (framework-bench.yml runs run.sh once per route or per framework, in parallel) into one report:
#   merge.sh part1.md part2.md ... > framework-bench.md
# Rows are ordered by framework, then mode, then the order of the parts; app rows measured by several parts keep the first.
set -euo pipefail
[ $# -gt 0 ] || { echo "usage: $0 part.md..." >&2; exit 2; }

# rows FILE...: the table rows (after the header and rule) of the "## $SECTION" section, prefixed with the part's index.
rows() {
  local i=0
  for f in "$@"; do
    i=$((i + 1))
    awk -v s="## $SECTION" -v i="$i" '$0 == s {on = 1; n = 0; next} /^## / {on = 0} on && /^\|/ {if (++n > 2) print i "\t" $0}' "$f"
  done
}
header() { awk -v s="## $SECTION" '$0 == s {on = 1; next} /^## / {on = 0} on && /^\|/ {print; if (++n == 2) exit}' "$1"; }
# Sort key: framework order, then mode, then part, then the row's place in its part.
order() {
  awk -F'\t' 'BEGIN {
      o["Inertia.Net (JIT)"] = 1; o["Inertia.Net (JIT, tuned)"] = 2; o["Inertia.Net (Native AOT)"] = 3; o["Inertia.Net (Native AOT, tuned)"] = 4
      o["Next.js"] = 5; o["Next.js (tuned)"] = 6; o["TanStack Start"] = 7; o["TanStack Start (tuned)"] = 8
      o["Nuxt"] = 9; o["Nuxt (tuned)"] = 10; o["Laravel (Octane, FrankenPHP)"] = 11; o["Laravel (Octane, FrankenPHP, tuned)"] = 12
      m["SSR"] = 1; m["SPA"] = 2; m["SPA (nearest)"] = 2 }
    { split($2, c, " \\| "); fw = substr(c[1], 3); mode = c[2]; if ('"$1"') mode = ""
      printf "%d\t%d\t%04d\t%06d\t%s\n", (fw in o ? o[fw] : 9), (mode in m ? m[mode] : 0), $1, NR, $2 }' |
    sort -t$'\t' -k1,1n -k2,2n -k3,3n -k4,4n | cut -f5
}

head -n1 "$1"
echo
echo "Split run: each part ran on its own GitHub runner, so numbers from different parts come from different machines."
i=0
for f in "$@"; do
  i=$((i + 1))
  echo "- Part $i ($(sed -n 's/^<!-- part: \(.*\) -->$/\1/p' "$f")): $(grep -m1 '^Host:' "$f" | sed 's/^Host: \([^.]*\)\..*/\1/')"
done
echo
grep -m1 '^Host:' "$1" | sed 's/^Host: [^.]*\. //'
echo
SECTION=Apps; echo "## Apps"; echo; header "$1"; rows "$@" | order 0 | awk -F' [|] ' '!seen[$1 $2]++'; echo
SECTION=Routes; echo "## Routes"; echo; awk '/^## Routes/ {on = 1; next} on && /^`/ {print} on && /^\| / {exit}' "$1"; echo
header "$1"; rows "$@" | order 0; echo
SECTION="Multi-core scaling"
if [ -n "$(rows "$@")" ]; then
  echo "## Multi-core scaling"; echo
  for f in "$@"; do grep -q '^## Multi-core scaling' "$f" && { header "$f"; break; }; done
  rows "$@" | order 1
fi
