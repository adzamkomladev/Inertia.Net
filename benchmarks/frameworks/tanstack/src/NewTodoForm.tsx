import { useState } from 'react'
import NewTodoPage, { emptyTodo } from './shared/NewTodoPage'
import type { FormErrors, NewTodo } from './shared/types'

// Posts the form as JSON to the /api/todos server route: a 422 shows its errors, a 201 starts a new todo.
export default function NewTodoForm() {
  const [values, setValues] = useState<NewTodo>(emptyTodo)
  const [errors, setErrors] = useState<FormErrors>({})
  const [processing, setProcessing] = useState(false)
  const submit = async () => {
    setProcessing(true)
    try {
      const response = await fetch('/api/todos', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) })
      if (response.status === 422) setErrors((await response.json()).errors)
      else if (response.ok) (setValues(emptyTodo), setErrors({}))
    } finally {
      setProcessing(false)
    }
  }
  return <NewTodoPage values={values} errors={errors} processing={processing} onChange={(field, value) => setValues((v) => ({ ...v, [field]: value }))} onSubmit={submit} />
}
