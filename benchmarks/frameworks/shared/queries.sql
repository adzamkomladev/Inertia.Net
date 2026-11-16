-- The two queries every app runs, word for word ($1 = perPage, $2 = offset).

-- /todos
SELECT id, title, done, priority, assignee, to_char(due_at, 'YYYY-MM-DD') AS due
FROM todos ORDER BY created_at DESC, id DESC LIMIT $1 OFFSET $2;

-- /dashboard (cached in Redis under bench:dashboard for 60 seconds)
SELECT assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS priority_sum
FROM todos GROUP BY assignee ORDER BY assignee;
