import '@fontsource-variable/inter'
import '@fontsource-variable/jetbrains-mono'
import './app.css'
import { createInertiaApp } from '@inertiajs/react'

// @inertiajs/vite wraps this call with the SSR server bootstrap when built with `vite build --ssr`.
createInertiaApp()
