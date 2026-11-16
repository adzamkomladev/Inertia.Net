'use client'
// BENCH_MODE=spa: the nearest Next.js gets to an SPA. The page is a static shell (prerendered at build time) that fetches its props
// from the /api route handler for the same path and renders the same component in the browser.
import { useEffect, useState } from 'react'
import ComputePage from '../shared/ComputePage.jsx'
import DashboardPage from '../shared/DashboardPage.jsx'
import HomePage from '../shared/HomePage.jsx'
import TodosPage from '../shared/TodosPage.jsx'

const pages = { home: HomePage, todos: TodosPage, dashboard: DashboardPage, compute: ComputePage }

export default function Spa({ page }) {
  const [props, setProps] = useState(null)
  useEffect(() => {
    const path = location.pathname === '/' ? '/home' : location.pathname
    fetch(`/api${path}${location.search}`).then((r) => r.json()).then(setProps)
  }, [])
  const Page = pages[page]
  return props ? <Page {...props} /> : <p>Loading…</p>
}
