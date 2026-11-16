import { createFileRoute } from '@tanstack/react-router'
import { getComputeParallel } from '../../data'
import ComputePage from '../../shared/ComputePage'

export const Route = createFileRoute('/compute/parallel')({
  validateSearch: (search: Record<string, unknown>) => search as Record<string, string>,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getComputeParallel({ data: deps.search }),
  component: () => <ComputePage {...Route.useLoaderData()} />,
})
