// The todo list: about 27 elements per row, so the perPage prop (10, 25 or 100) sets the DOM size.
import Layout from '../../Layout'

const priorities = ['Low', 'Normal', 'High']

function Row({ todo }) {
  return (
    <tr className={todo.done ? 'done' : undefined}>
      <td><input type="checkbox" checked={todo.done} readOnly aria-label={`Done: ${todo.title}`} /></td>
      <td>
        <a href={`/todos/${todo.id}`}>{todo.title}</a>
        <p className="note">{todo.note}</p>
      </td>
      <td>
        <ul className="tags">
          {todo.tags.map((tag) => (
            <li key={tag}><span>{tag}</span></li>
          ))}
        </ul>
      </td>
      <td>
        <span className="avatar">{todo.assignee.slice(0, 2).toUpperCase()}</span>
        <span>{todo.assignee}</span>
      </td>
      <td><time dateTime={todo.dueAt}>{todo.dueAt.slice(0, 10)}</time></td>
      <td><span className={`priority p${todo.priority}`}>{priorities[todo.priority]}</span></td>
      <td><span className="progress"><span style={{ width: `${todo.progress}%` }} /></span></td>
      <td>
        <button type="button">Edit</button>
        <button type="button">Delete</button>
      </td>
    </tr>
  )
}

export default function Index({ appName, auth, todos, pagination, filters }) {
  return (
    <Layout appName={appName}>
      <h1>Todos</h1>
      <p>Signed in as {auth.email}</p>
      <form>
        <input type="search" name="search" defaultValue={filters.search} aria-label="Search" />
        <select name="status" defaultValue={filters.status} aria-label="Status">
          <option value="all">All</option>
          <option value="open">Open</option>
          <option value="done">Done</option>
        </select>
        <button type="submit">Filter</button>
      </form>
      <table>
        <thead>
          <tr>
            <th>Done</th><th>Title</th><th>Tags</th><th>Assignee</th><th>Due</th><th>Priority</th><th>Progress</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {todos.map((todo) => <Row key={todo.id} todo={todo} />)}
        </tbody>
      </table>
      <nav aria-label="Pagination">
        <a href={`/todos?page=${pagination.page - 1}`}>Previous</a>
        <span>Page {pagination.page} of {Math.ceil(pagination.total / pagination.perPage)}</span>
        <a href={`/todos?page=${pagination.page + 1}`}>Next</a>
      </nav>
    </Layout>
  )
}
