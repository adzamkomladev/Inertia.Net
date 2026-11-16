// The props every app sends to the shared pages. Field names are the JSON names all four back ends produce.
export interface Feature {
  title: string
  body: string
}

export interface Todo {
  id: number
  title: string
  done: boolean
  priority: number
  assignee: string
  due: string // YYYY-MM-DD
}

export interface AssigneeStats {
  assignee: string
  total: number
  done: number
  prioritySum: number
}

export interface Stats {
  total: number
  done: number
  byAssignee: AssigneeStats[]
}

/** The body of POST /todos (Inertia apps) and POST /api/todos (Next.js, TanStack Start). */
export interface NewTodo {
  title: string
  priority: number
  assignee: string
  due: string
}

/** Field name to its first message, as Inertia's `errors` prop and the Node apps' 422 body carry it. */
export type FormErrors = Partial<Record<keyof NewTodo, string>>
