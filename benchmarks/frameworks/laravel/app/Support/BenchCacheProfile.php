<?php

namespace App\Support;

use Illuminate\Http\Request;
use Spatie\ResponseCache\CacheProfiles\CacheAllSuccessfulGetRequests;

/**
 * The response cache profile of the tuned app (BENCH_TUNED=1). Unlike spatie's default it caches Inertia visits too, so the
 * cache key tells an Inertia visit (JSON) from a first visit (HTML) at the same URL, and a page showing validation errors from one without.
 */
class BenchCacheProfile extends CacheAllSuccessfulGetRequests
{
    public function shouldCacheRequest(Request $request): bool
    {
        return $request->isMethod('GET') && ! $request->session()->has('errors');
    }

    public function useCacheNameSuffix(Request $request): string
    {
        $errors = $request->session()->get('errors');

        return $request->header('X-Inertia', '').($errors ? ':'.md5(serialize($errors->getBags())) : '');
    }
}
