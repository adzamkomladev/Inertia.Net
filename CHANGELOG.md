# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial solution scaffolding.
- `Inertia.Net.FastEndpoints`: `Send.InertiaAsync/InertiaLocationAsync/InertiaBackAsync` and `c.UseInertia()` (validation failures redirect back with errors; Precognition).
- `app.UseInertiaAntiforgeryCookie()`: the `XSRF-TOKEN` cookie for the client's `X-XSRF-TOKEN` header.
- `.WithInertiaPrecognition()` for Minimal API endpoints (laravel-precognition live validation).
