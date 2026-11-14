import '@fontsource-variable/inter'
import '@fontsource-variable/jetbrains-mono'
import './app.css'
import { createInertiaApp } from '@inertiajs/react'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and hydrates server-rendered pages.
createInertiaApp({
  serverHead: true, // title, description and social tags set on the server with Render(...).WithHead(...)
  progress: { color: 'var(--primary)', delay: 150 },
  // Page visits crossfade the article; app.css keeps the shell still and honours reduced motion.
  defaults: { visitOptions: (_href, options) => ({ viewTransition: true, ...options }) },
})
