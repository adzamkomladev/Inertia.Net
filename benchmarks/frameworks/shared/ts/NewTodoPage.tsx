import type { FormEvent } from 'react'
import Layout from './Layout'
import { ASSIGNEES } from './rules'
import type { FormErrors, NewTodo } from './types'

export interface NewTodoFormProps {
  values: NewTodo
  errors: FormErrors
  processing: boolean
  onChange: <K extends keyof NewTodo>(field: K, value: NewTodo[K]) => void
  onSubmit: () => void
}

// The new-todo form, without any data fetching: each app wraps it with its own form state (Inertia's useForm in the Inertia apps,
// a fetch to /api/todos in Next.js and TanStack Start), so the rendered markup is the same everywhere.
export default function NewTodoPage({ values, errors, processing, onChange, onSubmit }: NewTodoFormProps) {
  const submit = (e: FormEvent) => {
    e.preventDefault()
    onSubmit()
  }
  const error = (field: keyof NewTodo) => errors[field] && <p className="error" data-error={field}>{errors[field]}</p>
  return (
    <Layout title="New todo" page="new">
      <form onSubmit={submit} noValidate>
        <label>Title <input name="title" value={values.title} onChange={(e) => onChange('title', e.target.value)} /></label>
        {error('title')}
        <label>Priority <input name="priority" type="number" min={0} max={4} value={values.priority}
          onChange={(e) => onChange('priority', e.target.valueAsNumber)} /></label>
        {error('priority')}
        <label>Assignee <select name="assignee" value={values.assignee} onChange={(e) => onChange('assignee', e.target.value)}>
          {ASSIGNEES.map((a) => <option key={a}>{a}</option>)}
        </select></label>
        {error('assignee')}
        <label>Due <input name="due" type="date" value={values.due} onChange={(e) => onChange('due', e.target.value)} /></label>
        {error('due')}
        <button type="submit" disabled={processing}>Add todo</button>
      </form>
    </Layout>
  )
}

export const emptyTodo: NewTodo = { title: '', priority: 2, assignee: 'ada', due: '2026-06-01' }
