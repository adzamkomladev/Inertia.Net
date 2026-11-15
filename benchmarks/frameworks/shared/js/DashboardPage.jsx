import Layout from './Layout.jsx'

export default function DashboardPage({ stats }) {
  return (
    <Layout title="Dashboard" page="dashboard">
      <p>{stats.total} todos, {stats.done} done</p>
      <table>
        <thead>
          <tr><th>Assignee</th><th>Total</th><th>Done</th><th>Priority sum</th></tr>
        </thead>
        <tbody>
          {stats.byAssignee.map((a) => (
            <tr key={a.assignee}><td>{a.assignee}</td><td>{a.total}</td><td>{a.done}</td><td>{a.prioritySum}</td></tr>
          ))}
        </tbody>
      </table>
    </Layout>
  )
}
