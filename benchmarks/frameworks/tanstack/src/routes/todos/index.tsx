import { createFileRoute } from '@tanstack/react-router'
import { getTodos } from '../../data'
import TodosPage from '../../shared/TodosPage'

export const Route = createFileRoute('/todos/')({
  validateSearch: (search: Record<string, unknown>) => search as Record<string, string>,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getTodos({ data: deps.search }),
  component: () => <TodosPage {...Route.useLoaderData()} />,
})
