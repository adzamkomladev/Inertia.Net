<?php

// spatie/laravel-responsecache for the tuned app (BENCH_TUNED=1); merged over the package's defaults. Entries live in memory
// (config/cache.php), as in the other tuned apps. The routes set each page's lifetime and grace (stale-while-revalidate) with FlexibleCacheResponse.
return [
    'enabled' => env('BENCH_TUNED') === '1',
    'cache' => ['store' => 'memory', 'lifetime_in_seconds' => 60, 'tag' => ''],
    'excluded_request_headers' => [], // the default skips Inertia visits; BenchCacheProfile keys them apart instead
    'cache_profile' => App\Support\BenchCacheProfile::class,
];
