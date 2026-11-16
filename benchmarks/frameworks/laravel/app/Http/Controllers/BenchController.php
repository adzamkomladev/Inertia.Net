<?php

namespace App\Http\Controllers;

use App\Http\Requests\StoreTodoRequest;
use App\Models\Submission;
use App\Models\Todo;
use App\Support\Compute;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Cache;
use Inertia\Inertia;
use Inertia\Response;

// The same routes, queries and limits as the Inertia.Net, Next.js and TanStack Start apps (benchmarks/frameworks/README.md).
class BenchController
{
    private const FEATURES = [
        ['title' => 'Lists', 'body' => 'Group todos by project, tag and due date.'],
        ['title' => 'Reports', 'body' => 'See what got done this week and what slipped.'],
        ['title' => 'Teams', 'body' => 'Share lists and assign todos to people.'],
    ];

    public function home(): Response
    {
        return Inertia::render('Home', ['features' => self::FEATURES]);
    }

    public function todos(Request $request): Response
    {
        $perPage = self::int($request->query('perPage'), 25, 1, 100);
        $page = self::int($request->query('page'), 1, 1, 400);
        $todos = Todo::query()
            ->select(['id', 'title', 'done', 'priority', 'assignee', 'due_at'])
            ->orderByDesc('created_at')->orderByDesc('id')
            ->offset(($page - 1) * $perPage)->limit($perPage)
            ->get();

        return Inertia::render('Todos', ['todos' => $todos, 'page' => $page, 'perPage' => $perPage]);
    }

    // Inertia::once() only lets the client skip a prop it already holds; the 60 s server-side cache stays with Laravel's cache.
    public function dashboard(): Response
    {
        $stats = Cache::remember('bench:dashboard', 60, function () {
            $byAssignee = Todo::query()
                ->selectRaw('assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS "prioritySum"')
                ->groupBy('assignee')->orderBy('assignee')
                ->toBase()->get()->map(fn ($row) => (array) $row)->all();

            return [
                'total' => array_sum(array_column($byAssignee, 'total')),
                'done' => array_sum(array_column($byAssignee, 'done')),
                'byAssignee' => $byAssignee,
            ];
        });

        return Inertia::render('Dashboard', ['stats' => $stats]);
    }

    public function create(): Response
    {
        return Inertia::render('Todos/New');
    }

    public function store(StoreTodoRequest $request): RedirectResponse
    {
        $todo = $request->validated();
        Submission::create(['title' => $todo['title'], 'priority' => $todo['priority'], 'assignee' => $todo['assignee'], 'due_at' => $todo['due']]);

        return redirect('/todos/new', 303);
    }

    public function compute(Request $request): Response
    {
        $n = self::int($request->query('n'), 20000, 16, 1000000);

        return Inertia::render('Compute', ['n' => $n, 'checksum' => Compute::run($n), 'parallel' => false]);
    }

    // PHP has no standard in-request threads: the same batches run one after another (PHP scales across requests with more workers).
    public function computeParallel(Request $request): Response
    {
        $n = self::int($request->query('n'), 20000, 16, 1000000);

        return Inertia::render('Compute', ['n' => $n, 'checksum' => Compute::run($n), 'parallel' => true]);
    }

    private static function int(mixed $value, int $default, int $min, int $max): int
    {
        return is_numeric($value) ? max($min, min($max, (int) $value)) : $default;
    }
}
