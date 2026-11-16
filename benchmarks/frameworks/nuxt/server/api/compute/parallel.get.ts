import { computeOnPool } from '../../../shared/server'

export default defineEventHandler((event) => computeOnPool(getRequestURL(event).searchParams))
