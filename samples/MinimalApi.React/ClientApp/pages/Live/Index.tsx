import { router, useProp } from '@inertiajs/react'
import { useEffect, useState } from 'react'
import Layout from '../../Layout'

interface Counter {
  value: number
}

export default function Index() {
  // A live prop reads like any other; useProp also says when a live reload is in flight.
  const counter = useProp<Counter>('counter')
  const inbox = useProp<{ unread: number }>('inbox')
  const [hold, setHold] = useState(false)
  const [held, setHeld] = useState(0)

  // Returning false from the cancelable live event keeps the update back, e.g. to show a "new items" button instead.
  useEffect(() => {
    if (!hold) return
    return router.on('live', (event) => {
      if (!event.detail.props.includes('counter')) return
      setHeld((count) => count + 1)
      return false
    })
  }, [hold])

  return (
    <Layout>
      <h1>Live</h1>
      <p>
        Counter: <span data-testid="counter">{counter.value?.value}</span>
        {counter.loading && <span data-testid="counter-loading"> (updating...)</span>}
      </p>
      <p>
        Unread messages: <span data-testid="inbox">{inbox.value?.unread}</span>
      </p>
      <label>
        <input type="checkbox" data-testid="hold-updates" checked={hold} onChange={(e) => setHold(e.target.checked)} /> Hold
        updates
      </label>{' '}
      {hold && <span data-testid="held">{held} held</span>}
    </Layout>
  )
}
