import { createFileRoute } from '@tanstack/react-router'
import { getDashboard } from '../data.js'
import DashboardPage from '../shared/DashboardPage.jsx'

export const Route = createFileRoute('/dashboard')({
  loader: () => getDashboard(),
  component: () => <DashboardPage {...Route.useLoaderData()} />,
})
