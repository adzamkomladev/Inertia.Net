import { Deferred, router, useForm } from '@inertiajs/react'
import type { FormEvent } from 'react'
import Layout from '../../Layout'
import type { InertiaPages } from '../../types/inertia'

export default function Index({ todos, stats }: InertiaPages['Todos/Index']) {
  const form = useForm({ title: '' })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    form.post('/todos', { onSuccess: () => form.reset() })
  }

  return (
    <Layout>
      <h1>Todos</h1>
      <ul>
        {todos.map((todo) => (
          <li key={todo.id}>
            <label style={{ textDecoration: todo.done ? 'line-through' : undefined }}>
              <input type="checkbox" checked={todo.done} onChange={() => router.patch(`/todos/${todo.id}/toggle`)} /> {todo.title}
            </label>{' '}
            <button onClick={() => router.delete(`/todos/${todo.id}`)}>Delete</button>
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
