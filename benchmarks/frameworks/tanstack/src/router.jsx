import { createRouter } from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'

export function getRouter() {
  // The pending component is what SPA mode's prerendered shell shows until the client has loaded the route's data.
  return createRouter({ routeTree, defaultPendingComponent: () => <p>Loading…</p> })
}
