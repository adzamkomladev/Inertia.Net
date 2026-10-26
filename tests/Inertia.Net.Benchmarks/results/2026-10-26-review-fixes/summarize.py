"""Summarize the paired samples; timings and allocations are per fixture operation."""
import json
import random
import statistics
from pathlib import Path

root = Path(__file__).resolve().parent
rng = random.Random(20261026)
summary = []
hashes = None
for run in (1, 2):
    data = json.loads((root / f"paired-{run}.json").read_text())
    assert data["rounds"] == 32
    assert data["hashes"]["baseline"] != data["hashes"]["current"]
    assert hashes is None or hashes == data["hashes"]
    hashes = data["hashes"]
    for method in dict.fromkeys(row["Method"] for row in data["rows"]):
        variants = {
            name: {row["Round"]: row for row in data["rows"] if row["Method"] == method and row["Variant"] == name}
            for name in ("baseline", "current")
        }
        assert all(set(rows) == set(range(32)) for rows in variants.values())
        changes = [100 * (variants["current"][i]["Nanoseconds"] / variants["baseline"][i]["Nanoseconds"] - 1) for i in range(32)]
        bootstrap = sorted(statistics.median(rng.choices(changes, k=32)) for _ in range(5000))
        row = {"run": run, "method": method}
        for name in ("baseline", "current"):
            row[f"{name}Ms"] = statistics.median(r["Nanoseconds"] for r in variants[name].values()) / 1_000_000
            row[f"{name}Bytes"] = statistics.median(r["AllocatedBytes"] for r in variants[name].values())
        row["latencyChangePercent"] = statistics.median(changes)
        row["latency95PercentInterval"] = [bootstrap[124], bootstrap[4874]]
        row["allocationChangeBytes"] = row["currentBytes"] - row["baselineBytes"]
        summary.append(row)

(root / "summary.json").write_text(json.dumps({"rows": summary}, indent=2) + "\n")
for row in summary:
    print(f"run={row['run']} {row['method']}: {row['baselineBytes']:.0f} -> {row['currentBytes']:.0f} B; "
          f"{row['baselineMs']:.6f} -> {row['currentMs']:.6f} ms; "
          f"{row['latencyChangePercent']:+.1f}% {row['latency95PercentInterval']}")
