// Data for every route in the Node apps (Next.js, TanStack Start): the server-rendered pages and the JSON routes call the same functions.
// Each app caches dashboardStats() its own way (Next.js: 'use cache: remote', TanStack Start: a Redis helper).
import { count, desc, sql, sum } from 'drizzle-orm'
import { drizzle, type NodePgDatabase } from 'drizzle-orm/node-postgres'
import { boolean, date, integer, pgTable, serial, text, timestamp } from 'drizzle-orm/pg-core'
import pg from 'pg'
import { z } from 'zod'
import { compute, computeParallel } from './compute'
import { computeN, features, todosQuery } from './data'
import { ASSIGNEES, MESSAGES } from './rules'
import type { FormErrors, Stats, Todo } from './types'

// The tables of shared/schema.sql.
export const todosTable = pgTable('todos', {
  id: integer().primaryKey(),
  title: text().notNull(),
  done: boolean().notNull(),
  priority: integer().notNull(),
  assignee: text().notNull(),
  dueAt: date('due_at', { mode: 'string' }).notNull(),
  createdAt: timestamp('created_at', { withTimezone: true }).notNull(),
})

export const submissionsTable = pgTable('submissions', {
  id: serial().primaryKey(),
  title: text().notNull(),
  priority: integer().notNull(),
  assignee: text().notNull(),
  dueAt: date('due_at', { mode: 'string' }).notNull(),
  createdAt: timestamp('created_at', { withTimezone: true }).notNull().defaultNow(),
})

declare global {
  var __benchDb: NodePgDatabase | undefined
}

// On globalThis: Next.js may bundle this module into several route bundles, and one pool per process is what the other apps have.
const db = (globalThis.__benchDb ??= drizzle({ client: new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 10 }) }))

export const home = () => ({ features })

export async function todos(params: URLSearchParams): Promise<{ todos: Todo[]; page: number; perPage: number }> {
  const { perPage, page } = todosQuery(params)
  const t = todosTable
  const rows = await db
    .select({ id: t.id, title: t.title, done: t.done, priority: t.priority, assignee: t.assignee, due: t.dueAt })
    .from(t)
    .orderBy(desc(t.createdAt), desc(t.id))
    .limit(perPage)
    .offset((page - 1) * perPage)
  return { todos: rows, page, perPage }
}

export async function dashboardStats(): Promise<Stats> {
  const t = todosTable
  const byAssignee = await db
    .select({
      assignee: t.assignee,
      total: count(),
      done: sql<number>`count(*) filter (where ${t.done})`.mapWith(Number),
      prioritySum: sum(t.priority).mapWith(Number),
    })
    .from(t)
    .groupBy(t.assignee)
    .orderBy(t.assignee)
  return { total: byAssignee.reduce((s, r) => s + r.total, 0), done: byAssignee.reduce((s, r) => s + r.done, 0), byAssignee }
}

export function computeSequential(params: URLSearchParams) {
  const n = computeN(params)
  return { n, checksum: compute(n), parallel: false }
}

export async function computeOnPool(params: URLSearchParams) {
  const n = computeN(params)
  return { n, checksum: await computeParallel(n), parallel: true }
}

// The rules of shared/ts/rules.ts, one message per field whatever check failed.
const newTodo = z.object({
  title: z.string(MESSAGES.title).trim().min(3, MESSAGES.title).max(120, MESSAGES.title),
  priority: z.int(MESSAGES.priority).min(0, MESSAGES.priority).max(4, MESSAGES.priority),
  assignee: z.enum(ASSIGNEES, MESSAGES.assignee),
  due: z.iso.date(MESSAGES.due).refine((d) => d >= '2026-01-01' && d <= '2026-12-31', MESSAGES.due),
})

/** Validates a POST /api/todos body and inserts it: 201 with the new id, or 422 with one message per failing field. */
export async function createTodo(body: unknown): Promise<Response> {
  // A body that is not a JSON object fails every field, rather than answering with no field errors.
  const result = newTodo.safeParse(body !== null && typeof body === 'object' && !Array.isArray(body) ? body : {})
  if (!result.success) {
    const fields = z.flattenError(result.error).fieldErrors
    const errors: FormErrors = Object.fromEntries(Object.entries(fields).map(([field, messages]) => [field, messages?.[0]]))
    return Response.json({ errors }, { status: 422 })
  }
  const { title, priority, assignee, due } = result.data
  const [row] = await db.insert(submissionsTable).values({ title, priority, assignee, dueAt: due }).returning({ id: submissionsTable.id })
  return Response.json({ id: row.id }, { status: 201 })
}
