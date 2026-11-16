-- The one database every app reads: 10,000 todos, the same rows on every run.
CREATE TABLE todos (
  id integer PRIMARY KEY,
  title text NOT NULL,
  done boolean NOT NULL,
  priority integer NOT NULL,
  assignee text NOT NULL,
  due_at date NOT NULL,
  created_at timestamptz NOT NULL
);

INSERT INTO todos
SELECT i,
       'Todo ' || i,
       i % 3 = 0,
       i % 5,
       (ARRAY['ada', 'barbara', 'dennis', 'grace', 'linus'])[1 + i % 5],
       date '2026-01-01' + i % 365,
       timestamptz '2026-01-01 00:00:00+00' + i * interval '1 minute'
FROM generate_series(1, 10000) AS i;

-- /todos pages through this index, newest first.
CREATE INDEX todos_created_at_id ON todos (created_at DESC, id DESC);

ANALYZE todos;

-- Where POST /todos (POST /api/todos in the Node apps) writes. Separate from todos so the write runs never change what the read
-- routes return; run.sh empties it before each app starts.
CREATE TABLE submissions (
  id serial PRIMARY KEY,
  title text NOT NULL,
  priority integer NOT NULL,
  assignee text NOT NULL,
  due_at date NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
