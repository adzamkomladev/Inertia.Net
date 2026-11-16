import { createFileRoute } from '@tanstack/react-router'
import { getComputeParallel } from '../../data.js'
import ComputePage from '../../shared/ComputePage.jsx'

export const Route = createFileRoute('/compute/parallel')({
  validateSearch: (search) => search,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getComputeParallel({ data: deps.search }),
  component: () => <ComputePage {...Route.useLoaderData()} />,
})
