# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub:
[Report a vulnerability](https://github.com/adzamkomladev/Inertia.Net/security/advisories/new). Do not open a public issue.

Include the affected package and version, what an attacker needs (access, configuration), and steps or a test that reproduces it.
You will get an answer within a week. Fixes are released as a patch version and announced in a GitHub security advisory and the
changelog.

## Supported versions

Only the latest minor release receives security fixes.

## Scope

In scope: the packages published from `src/` (`Inertia.Net` and its `.Mvc`, `.FastEndpoints`, `.Live.SignalR`, `.Redis`,
`.EntityFrameworkCore`, `.MongoDB`, `.Testing` and `.Templates` packages) and the apps that `dotnet new inertia` creates.

Out of scope: the samples, the docs site, and the Inertia client packages (`@inertiajs/*`; report those to
[inertiajs/inertia](https://github.com/inertiajs/inertia/security)). The app's own SSR bundle and cache store (Redis) are trusted
by design: their output is written into pages unescaped.
