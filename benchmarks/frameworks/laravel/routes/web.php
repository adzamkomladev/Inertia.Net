<?php

use App\Http\Controllers\BenchController;
use Illuminate\Support\Facades\Route;

Route::get('/', [BenchController::class, 'home']);
Route::get('/todos', [BenchController::class, 'todos']);
Route::get('/dashboard', [BenchController::class, 'dashboard']);
Route::get('/compute', [BenchController::class, 'compute']);
Route::get('/compute/parallel', [BenchController::class, 'computeParallel']);
