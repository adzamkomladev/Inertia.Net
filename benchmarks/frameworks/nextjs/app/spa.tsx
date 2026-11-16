'use client'
// BENCH_MODE=spa: the nearest Next.js gets to an SPA. The page is a static shell (prerendered at build time) that fetches its props
// from the /api route handler for the same path and renders the same component in the browser.
import { useEffect, useState, type ComponentType } from 'react'
import ComputePage from '../shared/ComputePage'
import DashboardPage from '../shared/DashboardPage'
import HomePage from '../shared/HomePage'
import TodosPage from '../shared/TodosPage'
import NewTodoForm from './todos/new/form'

const pages: Record<string, ComponentType<any>> = { home: HomePage, todos: TodosPage, dashboard: DashboardPage, compute: ComputePage, new: NewTodoForm }

export default function Spa({ page }: { page: keyof typeof pages }) {
  const [props, setProps] = useState<object | null>(null)
  useEffect(() => {
    if (page === 'new') return setProps({}) // the form has no props to fetch
    const path = location.pathname === '/' ? '/home' : location.pathname
    fetch(`/api${path}${location.search}`).then((r) => r.json()).then(setProps)
  }, [page])
  const Page = pages[page]
  return props ? <Page {...props} /> : <p>Loading…</p>
}
