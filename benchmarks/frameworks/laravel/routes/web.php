<?php

use App\Http\Controllers\BenchController;
use Illuminate\Support\Facades\Route;
use Spatie\ResponseCache\Middlewares\FlexibleCacheResponse;

// BENCH_TUNED=1 turns the response cache on (config/responsecache.php), set like Inertia.Net tuned: pages for 60 s and todo
// lists for 10 s, each served stale while it refreshes. Compute and the form post are never cached.
$page = FlexibleCacheResponse::for(lifetime: 60, grace: 240);

Route::get('/', [BenchController::class, 'home'])->middleware($page);
Route::get('/todos', [BenchController::class, 'todos'])->middleware(FlexibleCacheResponse::for(lifetime: 10, grace: 60));
Route::get('/todos/new', [BenchController::class, 'create'])->middleware($page);
Route::post('/todos', [BenchController::class, 'store']);
Route::get('/dashboard', [BenchController::class, 'dashboard'])->middleware($page);
Route::get('/compute', [BenchController::class, 'compute']);
Route::get('/compute/parallel', [BenchController::class, 'computeParallel']);
