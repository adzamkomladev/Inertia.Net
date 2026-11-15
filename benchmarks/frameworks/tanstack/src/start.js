import { createStart } from '@tanstack/react-start'

// SPA mode: Start's spa option prerenders the shell (/_shell.html) for static hosts, but its Node server still renders every route
// unless SSR is off, so defaultSsr: false makes the server answer page requests with the shell, as an SPA host would.
export const startInstance = createStart(() => ({ defaultSsr: !__BENCH_SPA__ }))
