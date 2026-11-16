import { createFileRoute } from '@tanstack/react-router'
import { getCompute } from '../../data.js'
import ComputePage from '../../shared/ComputePage.jsx'

export const Route = createFileRoute('/compute/')({
  validateSearch: (search) => search,
  loaderDeps: ({ search }) => ({ search }),
  loader: ({ deps }) => getCompute({ data: deps.search }),
  component: () => <ComputePage {...Route.useLoaderData()} />,
})
