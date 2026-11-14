import { renderApp } from '@inertiaui/modal-react'

type Args = Parameters<typeof renderApp>

// Wraps the app in InertiaUI Modal's stack. @inertiaui/modal-react 3.1 types App and props more narrowly than @inertiajs/react 3 passes them.
export const withModals = (App: unknown, props: unknown) => renderApp(App as Args[0], props as Args[1])
