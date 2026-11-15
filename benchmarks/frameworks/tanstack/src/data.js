// Server functions: the route loaders call them (on the server in SSR mode, over RPC from the browser in SPA mode).
// Written out one by one: Start's compiler only recognizes createServerFn(...) chains it can see statically.
import { createServerFn } from '@tanstack/react-start'
import { computeOnPool, computeSequential, dashboard, home, todos } from './shared/server.js'

const search = (data) => data ?? {}

export const getHome = createServerFn({ method: 'GET' }).handler(() => home())
export const getTodos = createServerFn({ method: 'GET' }).validator(search).handler(({ data }) => todos(new URLSearchParams(data)))
export const getDashboard = createServerFn({ method: 'GET' }).handler(() => dashboard())
export const getCompute = createServerFn({ method: 'GET' }).validator(search).handler(({ data }) => computeSequential(new URLSearchParams(data)))
export const getComputeParallel = createServerFn({ method: 'GET' }).validator(search).handler(({ data }) => computeOnPool(new URLSearchParams(data)))
