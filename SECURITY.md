# Security policy

## Supported versions

Security fixes are released for the latest minor version of the four packages (`Inertia.Net`, `Inertia.Net.Mvc`,
`Inertia.Net.FastEndpoints`, `Inertia.Net.Testing`). Update all four together.

| Version | Supported |
|---|---|
| 1.x, latest minor | Yes |
| Older minors | No: upgrade to the latest minor |

## Reporting a vulnerability

Do not open a public issue. Report it privately through GitHub:
**Security > Report a vulnerability** on <https://github.com/adzamkomladev/Inertia.Net/security/advisories/new>.

Include the affected package and version, a description of the impact, and steps or code that reproduce it.
You should get an acknowledgement within 5 working days. A fix and a coordinated disclosure date are agreed with you
before details are published. Reporters are credited in the advisory unless they prefer otherwise.

## Scope notes

- The SSR server (Node) is trusted: its `head` and `body` are written into the page verbatim. Keep it on loopback
  (see "Server-side rendering" in the README).
- Root views, Vite manifest entries and component names are application configuration, not request input.
- CSRF protection needs `UseInertiaAntiforgery()` (see "CSRF and antiforgery" in the README).
