import Layout from './Layout'
import type { Todo } from './types'

export default function TodosPage({ todos, page, perPage }: { todos: Todo[]; page: number; perPage: number }) {
  return (
    <Layout title="Todos" page="todos">
      <p>Page {page}, {perPage} per page</p>
      <ul>
        {todos.map((t) => (
          <li key={t.id} data-todo={t.id}>
            <input type="checkbox" checked={t.done} readOnly /> {t.title} <em>{t.assignee}</em> priority {t.priority}, due {t.due}
          </li>
        ))}
      </ul>
      <a href={`/todos?perPage=${perPage}&page=${page + 1}`}>Next page</a>
    </Layout>
  )
}
