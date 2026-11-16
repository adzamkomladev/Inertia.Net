import { createFileRoute } from '@tanstack/react-router'
import NewTodoForm from '../../NewTodoForm'

export const Route = createFileRoute('/todos/new')({ component: NewTodoForm })
