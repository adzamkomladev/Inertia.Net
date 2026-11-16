import { createTodo } from '../../shared/server'

// The new-todo form posts JSON here: 201 with the new id, or 422 with one message per failing field.
export default defineEventHandler(async (event) => createTodo(await readBody(event).catch(() => null)))
