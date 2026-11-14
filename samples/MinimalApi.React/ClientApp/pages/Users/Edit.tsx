import { useForm } from '@inertiajs/react'
import { HeadlessModal } from '@inertiaui/modal-react'
import { useEffect, type FormEvent } from 'react'
import { routes } from '../../types/routes'

// Opened from the users list with <ModalLink> (GET /users/{id}/edit returns Modal("Users/Edit", ...)). HeadlessModal leaves the
// styling, and the open/close transition, to the app; the styled <Modal> expects Tailwind CSS.
export default function Edit({ user }: { user: { id: number; name: string } }) {
  const form = useForm({ name: user.name })

  return (
    <HeadlessModal>
      {({ close, isOpen, afterLeave }: { close: () => void; isOpen: boolean; afterLeave: () => void }) => {
        if (!isOpen) return <Left afterLeave={afterLeave} />
        const submit = (e: FormEvent) => {
          e.preventDefault()
          form.submit(routes.users.update(user.id), { onSuccess: () => close() })
        }
        return (
          <div role="dialog" aria-modal="true" aria-labelledby="edit-user-title" data-testid="user-modal"
            style={{ position: 'fixed', inset: 0, display: 'grid', placeItems: 'center', background: 'rgb(0 0 0 / 0.4)' }}>
            <form onSubmit={submit} style={{ background: 'white', padding: 16, borderRadius: 8, minWidth: 280 }}>
              <h2 id="edit-user-title">Edit {user.name}</h2>
              <label>
                Name
                <input data-testid="user-name" value={form.data.name} onChange={(e) => form.setData('name', e.target.value)} />
              </label>
              <div data-testid="error-user-name" role="alert">{form.errors.name}</div>
              <button type="submit" data-testid="user-save" disabled={form.processing}>Save</button>{' '}
              <button type="button" data-testid="user-cancel" onClick={close}>Cancel</button>
            </form>
          </div>
        )
      }}
    </HeadlessModal>
  )
}

// No leave transition: tell the modal stack at once that the modal is gone.
function Left({ afterLeave }: { afterLeave: () => void }) {
  useEffect(afterLeave, [afterLeave])
  return null
}
