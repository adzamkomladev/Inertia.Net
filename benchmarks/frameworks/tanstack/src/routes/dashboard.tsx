import { createFileRoute } from '@tanstack/react-router'
import { getDashboard } from '../data'
import DashboardPage from '../shared/DashboardPage'

export const Route = createFileRoute('/dashboard')({
  loader: () => getDashboard(),
  component: () => <DashboardPage {...Route.useLoaderData()} />,
})
