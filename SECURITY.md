# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub:
[Report a vulnerability](https://github.com/adzamkomladev/Inertia.Net/security/advisories/new). Do not open a public issue.

Include the affected package and version, the access and configuration an attacker needs, and steps or a test that reproduces the
vulnerability. You will get an answer within a week. Fixes are released in a patch version and announced in a GitHub security
advisory and the changelog.

## Supported versions

Only the latest minor release receives security fixes.

## Scope

This policy covers the packages published from `src/` (`Inertia.Net` and its `.Mvc`, `.FastEndpoints`, `.Live.SignalR`, `.Live.Sse`,
`.Redis`, `.Postgres`, `.EntityFrameworkCore`, `.MongoDB`, `.Testing` and `.Templates` packages) and the apps that `dotnet new inertia` creates.

This policy does not cover the samples, the docs site, or the Inertia client packages (`@inertiajs/*`). Report vulnerabilities in
those client packages to [inertiajs/inertia](https://github.com/inertiajs/inertia/security).
The app trusts its own SSR bundle and cache store (Redis) by design. It writes their output into pages without escaping it.
The app also trusts senders and readers on its configured Redis or PostgreSQL live backplane.
