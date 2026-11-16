import { useForm } from '@inertiajs/react'
import NewTodoPage, { emptyTodo } from '../../shared/NewTodoPage'

export default function New() {
  const form = useForm(emptyTodo)
  return (
    <NewTodoPage values={form.data} errors={form.errors} processing={form.processing}
      onChange={(field, value) => form.setData((data) => ({ ...data, [field]: value }))} onSubmit={() => form.post('/todos')} />
  )
}
