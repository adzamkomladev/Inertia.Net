import { Deferred, router, useForm } from '@inertiajs/react'
import type { FormEvent } from 'react'
import Layout from '../../Layout'
import type { InertiaPages } from '../../types/inertia'
import { routes } from '../../types/routes'

export default function Index({ todos, stats }: InertiaPages['Todos/Index']) {
  const form = useForm({ title: '' })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    form.submit(routes.todos.store(), { onSuccess: () => form.reset() })
  }

  return (
    <Layout>
      <h1>Todos</h1>
      <ul>
        {todos.map((todo) => (
          <li key={todo.id}>
            <label style={{ textDecoration: todo.done ? 'line-through' : undefined }}>
              <input type="checkbox" checked={todo.done} onChange={() => router.visit(routes.todos.toggle(todo.id))} /> {todo.title}
            </label>{' '}
            <button onClick={() => router.visit(routes.todos.destroy(todo.id))}>Delete</button>
          </li>
        ))}
      </ul>
      <form onSubmit={submit}>
        <input placeholder="What needs doing?" value={form.data.title} onChange={(e) => form.setData('title', e.target.value)} />
        <button type="submit" disabled={form.processing}>Add</button>
        {form.errors.title && <div style={{ color: 'crimson' }}>{form.errors.title}</div>}
      </form>
      <Deferred data="stats" fallback={<p>Counting...</p>}>
        <p>
          {stats?.done} of {stats?.total} done
        </p>
      </Deferred>
    </Layout>
  )
}
