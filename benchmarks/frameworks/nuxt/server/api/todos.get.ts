import { todos } from '../../shared/server'

export default defineEventHandler((event) => todos(getRequestURL(event).searchParams))
