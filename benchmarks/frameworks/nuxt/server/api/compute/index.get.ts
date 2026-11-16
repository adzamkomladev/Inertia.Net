import { computeSequential } from '../../../shared/server'

export default defineEventHandler((event) => computeSequential(getRequestURL(event).searchParams))
