import { createFileRoute } from '@tanstack/react-router'
import { getHome } from '../data.js'
import HomePage from '../shared/HomePage.jsx'

export const Route = createFileRoute('/')({
  loader: () => getHome(),
  component: () => <HomePage {...Route.useLoaderData()} />,
})
