// Client entry: hydrates the server-rendered page (or renders it, when SSR is off).
import { createInertiaApp } from '@inertiajs/react'

const pages = import.meta.glob('./pages/**/*.jsx')

createInertiaApp({ resolve: (name) => pages[`./pages/${name}.jsx`]() })
