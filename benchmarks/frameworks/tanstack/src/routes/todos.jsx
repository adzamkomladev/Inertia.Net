import { createFileRoute } from '@tanstack/react-router'
import { getTodos } from '../data.js'
import TodosPage from '../shared/TodosPage.jsx'

export const Route = createFileRoute('/todos')({
  validateSearch: (search) => search,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getTodos({ data: deps.search }),
  component: () => <TodosPage {...Route.useLoaderData()} />,
})
