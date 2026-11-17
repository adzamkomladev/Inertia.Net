Each app in Docker with --cpus 2 --memory 2g; 16 keep-alive connections, 5 s warmup, 10 s measured.
Host: 4 CPUs, AMD EPYC 9V74 80-Core Processor. "overloaded": at least one request failed or (SSR) was not server-rendered.

## Apps

| Framework | Mode | Image MB | Cold start ms | Memory at rest MB |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | SSR | 189 | 1104 | 64 |
| Inertia.Net (Native AOT) | SSR | 98 | 732 | 49 |
| Next.js | SSR | 186 | 1208 | 116 |
| Next.js (cluster) | SSR | 186 | 1674 | 195 |
| TanStack Start | SSR | 166 | 395 | 61 |
| TanStack Start (cluster) | SSR | 166 | 380 | 105 |
| Nuxt | SSR | 169 | 874 | 138 |
| Nuxt (cluster) | SSR | 169 | 978 | 277 |
| Laravel (Octane, FrankenPHP) | SSR | 312 | 1189 | 152 |

## Routes

`page` is a first visit (HTML); `json` is the data a client-side navigation fetches (an Inertia visit, or the app's /api route).
`POST` rows are the new-todo form: `valid` validates and inserts a row, `invalid` fails validation on all four fields.
CPU and peak memory are the whole container's (cgroup): every process, including Node SSR servers and PHP workers.

| Framework | Mode | Route | Response | Req/s | p50 ms | p99 ms | CPU µs per request | Peak memory MB | p50 / p99 ms at 50 req/s | p50 / p99 ms at 200 req/s |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | page | 911 | 13.564 | 53.186 | 2186 | 89 | 2.151 / 4.134 | 1.8 / 3.528 |
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | json | 1194 | 12.044 | 38.743 | 1667 | 85 | 1.764 / 3.391 | 1.536 / 3.671 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | page | 1026 | 10.28 | 52.143 | 1944 | 87 | 1.567 / 6.649 | 1.419 / 6.649 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | json | 1095 | 13.835 | 37.238 | 1790 | 68 | 1.285 / 6.918 | 0.848 / 1.91 |
| Inertia.Net (Native AOT) | SSR | `/compute?n=20000` | page | 765 | 17.202 | 63.562 | 2608 | 68 | 2.674 / 8.602 | 2.328 / 4.053 |
| Inertia.Net (Native AOT) | SSR | `/compute?n=20000` | json | 824 | 17.897 | 46.301 | 2410 | 57 | 2.067 / 3.391 | 1.836 / 3.599 |
| Inertia.Net (Native AOT) | SSR | `/compute/parallel?n=20000` | page | 783 | 14.394 | 58.721 | 2553 | 66 | 1.872 / 7.791 | 1.447 / 4.564 |
| Inertia.Net (Native AOT) | SSR | `/compute/parallel?n=20000` | json | 788 | 16.21 | 58.721 | 2521 | 56 | 1.73 / 7.488 | 1.211 / 3.072 |
| Next.js | SSR | `/compute?n=20000` | page | 112 | 134.897 | 239.556 | 10905 | 243 | 10.909 / 20.558 | 2630.439 / 4489.861 |
| Next.js | SSR | `/compute?n=20000` | json | 158 | 96.338 | 196.52 | 6587 | 206 | 6.391 / 8.433 | 280.678 / 4315.514 |
| Next.js | SSR | `/compute/parallel?n=20000` | page | 153 | 98.265 | 188.888 | 13036 | 305 | 8.774 / 18.62 | 1239.429 / 2157.876 |
| Next.js | SSR | `/compute/parallel?n=20000` | json | 253 | 62.315 | 92.597 | 7883 | 307 | 4.656 / 9.311 | 4.941 / 92.597 |
| Next.js (cluster) | SSR | `/compute?n=20000` | page | 165 | 94.449 | 212.719 | 12141 | 435 | 10.485 / 15.892 | 280.678 / 561.326 |
| Next.js (cluster) | SSR | `/compute?n=20000` | json | 349 | 46.301 | 112.876 | 5734 | 392 | 5.455 / 7.341 | 5.348 / 17.897 |
| Next.js (cluster) | SSR | `/compute/parallel?n=20000` | page | 174 | 89.002 | 154.954 | 11511 | 484 | 8.774 / 15.581 | 348.988 / 1481.233 |
| Next.js (cluster) | SSR | `/compute/parallel?n=20000` | json | 278 | 56.441 | 83.868 | 7201 | 485 | 6.022 / 9.311 | 6.519 / 21.817 |
| TanStack Start | SSR | `/compute?n=20000` | page | 176 | 83.868 | 185.185 | 6044 | 142 | 5.243 / 7.056 | 140.347 / 1635.4 |
| TanStack Start | SSR | `/compute?n=20000` | json | 212 | 70.177 | 143.154 | 4843 | 162 | 4.844 / 6.022 | 4.941 / 27.126 |
| TanStack Start | SSR | `/compute/parallel?n=20000` | page | 285 | 55.334 | 80.612 | 7026 | 242 | 3.819 / 6.649 | 3.325 / 8.774 |
| TanStack Start | SSR | `/compute/parallel?n=20000` | json | 335 | 46.301 | 63.562 | 5963 | 244 | 3.011 / 4.475 | 2.838 / 6.022 |
| TanStack Start (cluster) | SSR | `/compute?n=20000` | page | 332 | 48.172 | 115.133 | 6018 | 285 | 5.789 / 8.774 | 6.022 / 12.044 |
| TanStack Start (cluster) | SSR | `/compute?n=20000` | json | 389 | 35.792 | 80.612 | 5147 | 290 | 5.14 / 6.649 | 4.941 / 7.638 |
| TanStack Start (cluster) | SSR | `/compute/parallel?n=20000` | page | 281 | 57.57 | 90.782 | 7111 | 362 | 6.022 / 8.602 | 6.519 / 13.037 |
| TanStack Start (cluster) | SSR | `/compute/parallel?n=20000` | json | 316 | 50.118 | 66.13 | 6329 | 350 | 5.564 / 8.774 | 5.564 / 10.909 |
| Nuxt | SSR | `/compute?n=20000` | page | 179 | 87.257 | 124.624 | 6259 | 201 | 5.789 / 8.433 | 726.134 / 1215.127 |
| Nuxt | SSR | `/compute?n=20000` | json | 222 | 74.473 | 110.663 | 4531 | 197 | 4.749 / 5.904 | 4.656 / 22.698 |
| Nuxt | SSR | `/compute/parallel?n=20000` | page | 265 | 57.57 | 85.546 | 7534 | 262 | 4.387 / 9.687 | 3.819 / 41.937 |
| Nuxt | SSR | `/compute/parallel?n=20000` | json | 343 | 45.393 | 62.315 | 5814 | 265 | 2.952 / 4.564 | 2.782 / 4.475 |
| Nuxt (cluster) | SSR | `/compute?n=20000` | page | 288 | 53.186 | 94.449 | 6948 | 328 | 6.022 / 8.949 | 10.695 / 25.562 |
| Nuxt (cluster) | SSR | `/compute?n=20000` | json | 424 | 37.238 | 59.896 | 4714 | 322 | 5.039 / 6.782 | 4.941 / 9.128 |
| Nuxt (cluster) | SSR | `/compute/parallel?n=20000` | page | 245 | 62.315 | 137.595 | 8170 | 391 | 6.519 / 13.037 | 7.638 / 27.126 |
| Nuxt (cluster) | SSR | `/compute/parallel?n=20000` | json | 328 | 47.227 | 63.562 | 6089 | 399 | 5.455 / 9.497 | 5.564 / 15.275 |
| Laravel (Octane, FrankenPHP) | SSR | `/compute?n=20000` | page | 61 | 269.779 | 303.815 | 32895 | 168 | 20.155 / 23.615 | 4147.937 / 7366.092 |
| Laravel (Octane, FrankenPHP) | SSR | `/compute?n=20000` | json | 67 | 216.973 | 275.175 | 29846 | 167 | 18.255 / 20.969 | 4066.605 / 7221.659 |
| Laravel (Octane, FrankenPHP) | SSR | `/compute/parallel?n=20000` | page | 63 | 230.254 | 286.292 | 31764 | 166 | 19.76 / 22.698 | 4147.937 / 7366.092 |
| Laravel (Octane, FrankenPHP) | SSR | `/compute/parallel?n=20000` | json | 67 | 216.973 | 275.175 | 29859 | 164 | 18.255 / 24.087 | 3986.868 / 7221.659 |

## Multi-core scaling

| Framework | Route | CPUs | Req/s (16 connections) | p50 ms (1 connection) |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | `/compute?n=20000` | 1 | 662 | 1.663 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 1 | 778 | 0.918 |
| Inertia.Net (JIT) | `/compute?n=20000` | 2 | 1222 | 1.364 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 2 | 1199 | 0.848 |
| Inertia.Net (JIT) | `/compute?n=20000` | 4 | 1930 | 1.419 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 4 | 2041 | 0.738 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 1 | 598 | 1.73 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 1 | 597 | 1.164 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 2 | 939 | 1.73 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 2 | 885 | 1.097 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 4 | 1457 | 1.764 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 4 | 1472 | 0.865 |
| Next.js | `/compute?n=20000` | 1 | 179 | 5.039 |
| Next.js | `/compute/parallel?n=20000` | 1 | 133 | 7.488 |
| Next.js | `/compute?n=20000` | 2 | 161 | 6.266 |
| Next.js | `/compute/parallel?n=20000` | 2 | 280 | 3.896 |
| Next.js | `/compute?n=20000` | 4 | 194 | 5.039 |
| Next.js | `/compute/parallel?n=20000` | 4 | 384 | 2.952 |
| Next.js (cluster) | `/compute?n=20000` | 1 | 179 | 5.039 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 1 | 167 | 5.564 |
| Next.js (cluster) | `/compute?n=20000` | 2 | 330 | 5.348 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 2 | 257 | 6.022 |
| Next.js (cluster) | `/compute?n=20000` | 4 | 445 | 5.039 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 4 | 378 | 5.564 |
| TanStack Start | `/compute?n=20000` | 1 | 208 | 4.656 |
| TanStack Start | `/compute/parallel?n=20000` | 1 | 178 | 5.243 |
| TanStack Start | `/compute?n=20000` | 2 | 208 | 4.749 |
| TanStack Start | `/compute/parallel?n=20000` | 2 | 315 | 2.952 |
| TanStack Start | `/compute?n=20000` | 4 | 210 | 4.749 |
| TanStack Start | `/compute/parallel?n=20000` | 4 | 418 | 2.622 |
| TanStack Start (cluster) | `/compute?n=20000` | 1 | 201 | 4.656 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 1 | 174 | 5.455 |
| TanStack Start (cluster) | `/compute?n=20000` | 2 | 389 | 4.656 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 2 | 300 | 5.348 |
| TanStack Start (cluster) | `/compute?n=20000` | 4 | 487 | 4.844 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 4 | 443 | 5.348 |
| Nuxt | `/compute?n=20000` | 1 | 210 | 4.656 |
| Nuxt | `/compute/parallel?n=20000` | 1 | 177 | 5.455 |
| Nuxt | `/compute?n=20000` | 2 | 215 | 4.749 |
| Nuxt | `/compute/parallel?n=20000` | 2 | 335 | 2.838 |
| Nuxt | `/compute?n=20000` | 4 | 213 | 4.749 |
| Nuxt | `/compute/parallel?n=20000` | 4 | 427 | 2.52 |
| Nuxt (cluster) | `/compute?n=20000` | 1 | 209 | 4.656 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 1 | 185 | 5.243 |
| Nuxt (cluster) | `/compute?n=20000` | 2 | 408 | 4.564 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 2 | 316 | 5.14 |
| Nuxt (cluster) | `/compute?n=20000` | 4 | 519 | 4.749 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 4 | 452 | 5.348 |
| Laravel (Octane, FrankenPHP) | `/compute?n=20000` | 1 | 49 | 19.372 |
| Laravel (Octane, FrankenPHP) | `/compute/parallel?n=20000` (sequential in PHP) | 1 | 49 | 18.993 |
| Laravel (Octane, FrankenPHP) | `/compute?n=20000` | 2 | 66 | 19.372 |
| Laravel (Octane, FrankenPHP) | `/compute/parallel?n=20000` (sequential in PHP) | 2 | 66 | 19.372 |
| Laravel (Octane, FrankenPHP) | `/compute?n=20000` | 4 | 129 | 19.372 |
| Laravel (Octane, FrankenPHP) | `/compute/parallel?n=20000` (sequential in PHP) | 4 | 128 | 19.372 |
