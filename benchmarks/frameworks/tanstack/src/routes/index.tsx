import { createFileRoute } from '@tanstack/react-router'
import { getHome } from '../data'
import HomePage from '../shared/HomePage'

export const Route = createFileRoute('/')({
  loader: () => getHome(),
  component: () => <HomePage {...Route.useLoaderData()} />,
})
