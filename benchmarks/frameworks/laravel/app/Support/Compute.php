<?php

namespace App\Support;

/**
 * The CPU-heavy work behind /compute. Same algorithm as compute.js (Node apps) and Compute.cs (Inertia.Net): 16 batches of n/16 records
 * from a xorshift32 generator, each sorted by (value, id), grouped into 64 buckets and hashed with SHA-256; the checksum is the SHA-256
 * of the 16 batch digests. PHP has no standard in-request threads, so /compute/parallel runs the same batches one after another.
 */
final class Compute
{
    public const BATCHES = 16;

    public static function run(int $n): string
    {
        $m = intdiv($n, self::BATCHES);
        $digests = '';
        for ($b = 0; $b < self::BATCHES; $b++) {
            $digests .= self::batch($b, $m);
        }

        return hash('sha256', $digests);
    }

    private static function batch(int $b, int $m): string
    {
        $s = (($b + 1) * 2654435761) & 0xFFFFFFFF;
        $records = [];
        for ($i = 0; $i < $m; $i++) {
            $s ^= ($s << 13) & 0xFFFFFFFF;
            $s ^= $s >> 17;
            $s ^= ($s << 5) & 0xFFFFFFFF;
            $value = $s % 1000000;
            $s ^= ($s << 13) & 0xFFFFFFFF;
            $s ^= $s >> 17;
            $s ^= ($s << 5) & 0xFFFFFFFF;
            $records[] = [$value, $b * $m + $i, $s % 64];
        }

        usort($records, fn (array $x, array $y) => $x[0] <=> $y[0] ?: $x[1] <=> $y[1]);
        $counts = array_fill(0, 64, 0);
        $sums = array_fill(0, 64, 0);
        $ids = [];
        foreach ($records as [$value, $id, $group]) {
            $counts[$group]++;
            $sums[$group] += $value;
            $ids[] = $id;
        }

        $groups = [];
        foreach ($counts as $g => $c) {
            $groups[] = $g.':'.$c.':'.$sums[$g];
        }

        return hash('sha256', implode(',', $ids).'|'.implode(';', $groups));
    }
}
