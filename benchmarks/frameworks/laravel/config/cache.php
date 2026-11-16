<?php

use Illuminate\Support\Str;

// Replaces the skeleton's config/cache.php with the stores the benchmark uses. `memory` is an in-process array store for the tuned
// app's response cache (one per Octane worker, like the Node apps' per-process caches); Octane flushes only the store named `array`.
return [
    'default' => env('CACHE_STORE', 'redis'),
    'stores' => [
        'array' => ['driver' => 'array', 'serialize' => false],
        'memory' => ['driver' => 'array', 'serialize' => false],
        'redis' => ['driver' => 'redis', 'connection' => 'cache', 'lock_connection' => 'default'],
    ],
    'prefix' => env('CACHE_PREFIX', Str::slug((string) env('APP_NAME', 'laravel')).'-cache-'),
];
