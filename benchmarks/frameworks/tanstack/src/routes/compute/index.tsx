import { createFileRoute } from '@tanstack/react-router'
import { getCompute } from '../../data'
import ComputePage from '../../shared/ComputePage'

export const Route = createFileRoute('/compute/')({
  validateSearch: (search: Record<string, unknown>) => search as Record<string, string>,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getCompute({ data: deps.search }),
  component: () => <ComputePage {...Route.useLoaderData()} />,
})
