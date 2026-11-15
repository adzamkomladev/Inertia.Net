// Shared page chrome: 17 elements around every page.
export default function Layout({ appName, children }) {
  return (
    <div className="shell">
      <header>
        <a href="/" className="brand">{appName}</a>
        <nav>
          <ul>
            <li><a href="/">Home</a></li>
            <li><a href="/todos">Todos</a></li>
            <li><a href="/projects">Projects</a></li>
            <li><a href="/reports">Reports</a></li>
            <li><a href="/settings">Settings</a></li>
          </ul>
        </nav>
      </header>
      <main>{children}</main>
      <footer>
        <p>{appName} hosting probe</p>
      </footer>
    </div>
  )
}
