"""Summarize the paired samples; timings and allocations are per fixture operation."""
import json
import random
import statistics
from pathlib import Path

root = Path(__file__).resolve().parent
rng = random.Random(20261104)
summary = []
hashes = None
for run in (1, 2):
    data = json.loads((root / f"paired-{run}.json").read_text())
    assert data["rounds"] == 32
    assert set(data["hashes"]) == {"benchmarks", "core"}
    affinity = int(data["environment"]["processAffinity"], 16)
    assert affinity > 0 and affinity & (affinity - 1) == 0, "Pin each process to one CPU."
    assert data["environment"]["processorCount"] == 1
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

acceptance = {
    "render": all(row["allocationChangeBytes"] <= 64 and row["latencyChangePercent"] <= 5
                  for row in summary if row["method"] in ("Full", "PartialPrepend")),
    # Object counts are corroborated by the source: one PageRequest record on this path,
    # and a pooled-buffer wrapper, Utf8JsonWriter and returned base64url string for the cursor.
    "garbageRequestBytes": [row["currentBytes"] for row in summary if row["method"] == "RequestGarbage"],
    "cursorRoundTripBytes": [row["currentBytes"] for row in summary if row["method"] == "CursorRoundTrip"],
    "allocationCountEvidence": {
        "garbageRequest": "One PageRequest record; no other allocation in PageRequest.From (source inspection).",
        "cursorRoundTrip": "Three objects: PooledBufferWriter, Utf8JsonWriter, base64url output string; warmed pooled byte array, stack decode and unboxed value types (source inspection).",
        "limitation": "The probe measures allocated bytes, not runtime object counts; allocation counts are corroborated by source inspection."
    },
}
(root / "summary.json").write_text(json.dumps({"rows": summary, "acceptance": acceptance}, indent=2) + "\n")
for row in summary:
    print(f"run={row['run']} {row['method']}: {row['baselineBytes']:.0f} -> {row['currentBytes']:.0f} B; "
          f"{row['baselineMs']:.6f} -> {row['currentMs']:.6f} ms; "
          f"{row['latencyChangePercent']:+.1f}% {row['latency95PercentInterval']}")
