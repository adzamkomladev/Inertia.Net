Each app in Docker with --cpus 2 --memory 2g; 16 keep-alive connections, 5 s warmup, 10 s measured.

Split run: each part ran on its own GitHub runner, so numbers from different parts come from different machines.
- Part 1 (--routes home): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 2 (--routes todos25): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 3 (--routes todos100): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 4 (--routes dashboard): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 5 (--routes new): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 6 (--routes create): 4 CPUs, AMD EPYC 9V74 80-Core Processor
- Part 7 (--routes invalid): 4 CPUs, AMD EPYC 9V45 96-Core Processor
- Part 8 (--routes compute): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 9 (--routes parallel): 4 CPUs, AMD EPYC 7763 64-Core Processor

"overloaded": at least one request failed or (SSR) was not server-rendered.

## Apps

| Framework | Mode | Image MB | Cold start ms | Memory at rest MB |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | SSR | 189 | 1055 | 58 |
| Inertia.Net (JIT) | SPA | 125 | 478 | 27 |
| Next.js | SSR | 186 | 731 | 78 |
| Next.js | SPA (nearest) | 186 | 659 | 73 |
| Next.js (cluster) | SSR | 186 | 980 | 160 |
| Next.js (cluster) | SPA (nearest) | 186 | 864 | 151 |
| TanStack Start | SSR | 166 | 285 | 30 |
| TanStack Start | SPA | 166 | 288 | 28 |
| TanStack Start (cluster) | SSR | 166 | 296 | 68 |
| TanStack Start (cluster) | SPA | 166 | 301 | 66 |
| Nuxt | SSR | 169 | 714 | 139 |
| Nuxt | SPA | 169 | 296 | 138 |
| Nuxt (cluster) | SSR | 169 | 786 | 288 |
| Nuxt (cluster) | SPA | 169 | 355 | 184 |

## Routes

`page` is a first visit (HTML); `json` is the data a client-side navigation fetches (an Inertia visit, or the app's /api route).
`POST` rows are the new-todo form: `valid` validates and inserts a row, `invalid` fails validation on all four fields.

| Framework | Mode | Route | Response | Req/s | p50 ms | p99 ms | CPU µs per request | Peak memory MB | p50 / p99 ms at 50 req/s | p50 / p99 ms at 200 req/s |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Inertia.Net (JIT) | SSR | `/` | page | 9876 | 1.141 | 17.546 | 200 | 88 | 0.669 / 1.391 | 0.538 / 1.235 |
| Inertia.Net (JIT) | SSR | `/` | json | 24344 | 0.618 | 1.598 | 74 | 87 | 0.432 / 0.918 | 0.355 / 0.936 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=25` | page | 1717 | 6.649 | 44.503 | 1161 | 147 | 1.187 / 2.238 | 1.034 / 2.374 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=25` | json | 5954 | 2.57 | 4.844 | 298 | 139 | 0.974 / 1.63 | 0.738 / 1.285 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=100` | page | 591 | 22.698 | 61.094 | 3380 | 192 | 2.47 / 3.459 | 1.872 / 3.011 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=100` | json | 3006 | 5.14 | 10.28 | 568 | 182 | 1.567 / 2.108 | 1.211 / 1.836 |
| Inertia.Net (JIT) | SSR | `/dashboard` | page | 6463 | 1.696 | 25.06 | 307 | 121 | 0.993 / 1.764 | 0.669 / 1.506 |
| Inertia.Net (JIT) | SSR | `/dashboard` | json | 19875 | 0.738 | 1.8 | 97 | 110 | 0.696 / 1.506 | 0.468 / 1.337 |
| Inertia.Net (JIT) | SSR | `/todos/new` | page | 6812 | 1.836 | 15.581 | 288 | 150 | 1.097 / 1.63 | 0.582 / 1.337 |
| Inertia.Net (JIT) | SSR | `/todos/new` | json | 23881 | 0.63 | 1.391 | 75 | 146 | 0.432 / 0.865 | 0.348 / 0.918 |
| Inertia.Net (JIT) | SSR | `POST /todos` | valid | 2878 | 4.749 | 15.581 | 596 | 106 | 1.235 / 1.948 | 0.799 / 17.897 |
| Inertia.Net (JIT) | SSR | `POST /todos` | invalid | 18254 | 0.696 | 3.26 | 101 | 88 | 0.45 / 0.753 | 0.239 / 0.882 |
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | page | 641 | 19.76 | 67.452 | 3114 | 85 | 2.374 / 3.599 | 2.108 / 3.391 |
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | json | 1029 | 14.112 | 40.308 | 1928 | 85 | 1.987 / 3.325 | 1.836 / 3.011 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | page | 636 | 18.993 | 106.365 | 3139 | 88 | 1.764 / 6.519 | 1.536 / 3.973 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | json | 943 | 15.892 | 41.937 | 2102 | 81 | 1.567 / 3.072 | 1.26 / 2.57 |
| Inertia.Net (JIT) | SPA | `/` | page | 63008 | 0.196 | 1.164 | 23 | 42 | 0.369 / 0.783 | 0.264 / 0.815 |
| Inertia.Net (JIT) | SPA | `/` | json | 29562 | 0.507 | 1.598 | 62 | 45 | 0.606 / 1.075 | 0.348 / 0.9 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=25` | page | 5518 | 2.374 | 12.531 | 329 | 72 | 0.918 / 1.447 | 0.643 / 1.187 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=25` | json | 6357 | 2.374 | 4.844 | 273 | 70 | 1.164 / 1.764 | 0.71 / 1.285 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=100` | page | 2145 | 6.266 | 31.783 | 876 | 77 | 1.476 / 2.674 | 1.164 / 1.872 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=100` | json | 3035 | 5.039 | 10.28 | 576 | 76 | 1.536 / 1.987 | 1.097 / 1.987 |
| Inertia.Net (JIT) | SPA | `/dashboard` | page | 33285 | 0.441 | 1.337 | 50 | 80 | 0.606 / 1.447 | 0.384 / 1.164 |
| Inertia.Net (JIT) | SPA | `/dashboard` | json | 20042 | 0.738 | 1.948 | 96 | 66 | 0.594 / 1.391 | 0.459 / 1.391 |
| Inertia.Net (JIT) | SPA | `/todos/new` | page | 64373 | 0.192 | 1.119 | 23 | 42 | 0.321 / 0.799 | 0.258 / 0.815 |
| Inertia.Net (JIT) | SPA | `/todos/new` | json | 29779 | 0.507 | 1.337 | 63 | 45 | 0.384 / 0.865 | 0.334 / 0.882 |
| Inertia.Net (JIT) | SPA | `POST /todos` | valid | 3145 | 4.217 | 23.152 | 582 | 76 | 1.013 / 2.238 | 0.783 / 164.439 |
| Inertia.Net (JIT) | SPA | `POST /todos` | invalid | 18992 | 0.696 | 2.674 | 98 | 55 | 0.432 / 1.419 | 0.248 / 0.974 |
| Inertia.Net (JIT) | SPA | `/compute?n=20000` | page | 970 | 14.394 | 52.143 | 2044 | 46 | 1.872 / 3.133 | 1.8 / 2.674 |
| Inertia.Net (JIT) | SPA | `/compute?n=20000` | json | 939 | 15.892 | 41.114 | 2107 | 48 | 2.027 / 3.196 | 1.91 / 2.952 |
| Inertia.Net (JIT) | SPA | `/compute/parallel?n=20000` | page | 948 | 12.782 | 56.441 | 2101 | 55 | 1.337 / 6.918 | 1.141 / 2.47 |
| Inertia.Net (JIT) | SPA | `/compute/parallel?n=20000` | json | 924 | 11.808 | 53.186 | 2153 | 43 | 1.337 / 2.782 | 1.187 / 2.52 |
| Next.js | SSR | `/` | page | 522 | 29.362 | 49.135 | 2730 | 183 | 3.671 / 7.947 | 3.196 / 10.485 |
| Next.js | SSR | `/` | json | 2235 | 6.519 | 14.682 | 553 | 188 | 0.936 / 1.391 | 0.63 / 1.187 |
| Next.js | SSR | `/todos?perPage=25` | page | 357 | 42.775 | 67.452 | 4082 | 187 | 4.217 / 8.774 | 3.819 / 20.969 |
| Next.js | SSR | `/todos?perPage=25` | json | 1502 | 9.881 | 22.698 | 814 | 187 | 1.419 / 3.819 | 1.141 / 3.011 |
| Next.js | SSR | `/todos?perPage=100` | page | 143 | 104.28 | 167.728 | 10168 | 194 | 7.791 / 17.897 | 1571.896 / 2683.048 |
| Next.js | SSR | `/todos?perPage=100` | json | 716 | 21.389 | 40.308 | 1660 | 190 | 2.282 / 3.896 | 1.696 / 4.134 |
| Next.js | SSR | `/dashboard` | page | 478 | 32.418 | 56.441 | 3087 | 197 | 3.819 / 8.602 | 3.459 / 19.372 |
| Next.js | SSR | `/dashboard` | json | 2708 | 5.243 | 16.534 | 479 | 201 | 1.097 / 1.872 | 0.865 / 1.567 |
| Next.js | SSR | `/todos/new` | page | 497 | 30.548 | 54.249 | 2795 | 181 | 3.528 / 7.947 | 3.26 / 12.531 |
| Next.js | SSR | `POST /api/todos` | valid | 1490 | 9.497 | 25.06 | 855 | 170 | 1.598 / 4.134 | 0.974 / 38.743 |
| Next.js | SSR | `POST /api/todos` | invalid | 2250 | 5.904 | 17.546 | 572 | 179 | 0.768 / 1.506 | 0.507 / 1.419 |
| Next.js | SSR | `/compute?n=20000` | page | 104 | 146.017 | 230.254 | 11709 | 186 | 10.695 / 17.897 | 2847.272 / 5056.312 |
| Next.js | SSR | `/compute?n=20000` | json | 156 | 94.449 | 196.52 | 6759 | 180 | 6.649 / 9.687 | 1167.942 / 5056.312 |
| Next.js | SSR | `/compute/parallel?n=20000` | page | 143 | 106.365 | 161.214 | 13975 | 289 | 8.602 / 16.21 | 1735.5 / 3021.547 |
| Next.js | SSR | `/compute/parallel?n=20000` | json | 219 | 71.581 | 106.365 | 9102 | 300 | 5.348 / 8.433 | 5.564 / 64.833 |
| Next.js | SPA (nearest) | `/` | page | 3443 | 4.134 | 10.695 | 334 | 155 | 0.71 / 1.097 | 0.571 / 1.075 |
| Next.js | SPA (nearest) | `/` | json | 2301 | 6.266 | 14.976 | 543 | 165 | 0.9 / 1.337 | 0.655 / 1.164 |
| Next.js | SPA (nearest) | `/todos?perPage=25` | page | 3088 | 4.564 | 11.577 | 377 | 158 | 1.054 / 1.536 | 0.643 / 1.187 |
| Next.js | SPA (nearest) | `/todos?perPage=25` | json | 1594 | 9.128 | 21.817 | 789 | 170 | 1.506 / 2.422 | 0.974 / 2.282 |
| Next.js | SPA (nearest) | `/todos?perPage=100` | page | 2026 | 6.782 | 17.546 | 592 | 164 | 1.164 / 1.836 | 0.696 / 1.034 |
| Next.js | SPA (nearest) | `/todos?perPage=100` | json | 842 | 17.546 | 44.503 | 1462 | 174 | 2.282 / 4.217 | 1.536 / 3.671 |
| Next.js | SPA (nearest) | `/dashboard` | page | 3275 | 4.301 | 11.35 | 353 | 161 | 0.993 / 1.663 | 0.643 / 1.097 |
| Next.js | SPA (nearest) | `/dashboard` | json | 2784 | 5.039 | 14.112 | 472 | 166 | 1.419 / 3.744 | 0.918 / 2.674 |
| Next.js | SPA (nearest) | `/todos/new` | page | 3154 | 4.475 | 11.127 | 362 | 152 | 0.799 / 1.337 | 0.571 / 1.075 |
| Next.js | SPA (nearest) | `POST /api/todos` | valid | 1507 | 9.311 | 23.615 | 843 | 170 | 1.506 / 4.053 | 1.013 / 19.76 |
| Next.js | SPA (nearest) | `POST /api/todos` | invalid | 2378 | 5.675 | 15.892 | 550 | 170 | 0.753 / 1.948 | 0.582 / 2.027 |
| Next.js | SPA (nearest) | `/compute?n=20000` | page | 2060 | 6.782 | 17.202 | 577 | 162 | 0.815 / 1.211 | 0.669 / 1.034 |
| Next.js | SPA (nearest) | `/compute?n=20000` | json | 157 | 94.449 | 196.52 | 6636 | 162 | 6.649 / 9.687 | 1078.998 / 4957.169 |
| Next.js | SPA (nearest) | `/compute/parallel?n=20000` | page | 1943 | 7.056 | 18.62 | 619 | 181 | 0.882 / 1.91 | 0.738 / 1.447 |
| Next.js | SPA (nearest) | `/compute/parallel?n=20000` | json | 223 | 70.177 | 92.597 | 8952 | 270 | 5.14 / 7.638 | 4.941 / 46.301 |
| Next.js (cluster) | SSR | `/` | page | 600 | 20.969 | 73.012 | 3333 | 356 | 3.528 / 7.341 | 3.196 / 9.881 |
| Next.js (cluster) | SSR | `/` | json | 2972 | 4.301 | 30.548 | 673 | 379 | 0.831 / 1.663 | 0.753 / 1.663 |
| Next.js (cluster) | SSR | `/todos?perPage=25` | page | 395 | 33.728 | 104.28 | 5066 | 381 | 5.455 / 10.695 | 4.134 / 16.865 |
| Next.js (cluster) | SSR | `/todos?perPage=25` | json | 1740 | 7.488 | 41.937 | 1150 | 383 | 1.872 / 3.973 | 1.187 / 3.325 |
| Next.js (cluster) | SSR | `/todos?perPage=100` | page | 160 | 90.782 | 192.666 | 12476 | 422 | 8.268 / 17.897 | 740.657 / 2683.048 |
| Next.js (cluster) | SSR | `/todos?perPage=100` | json | 995 | 14.394 | 46.301 | 2004 | 376 | 2.328 / 3.744 | 1.872 / 3.973 |
| Next.js (cluster) | SSR | `/dashboard` | page | 503 | 26.073 | 80.612 | 3975 | 398 | 4.656 / 9.881 | 3.671 / 16.865 |
| Next.js (cluster) | SSR | `/dashboard` | json | 2834 | 3.973 | 39.518 | 704 | 410 | 1.506 / 3.133 | 0.9 / 1.91 |
| Next.js (cluster) | SSR | `/todos/new` | page | 562 | 23.152 | 83.868 | 3554 | 366 | 3.599 / 7.791 | 3.325 / 9.128 |
| Next.js (cluster) | SSR | `POST /api/todos` | valid | 1656 | 7.488 | 57.57 | 1156 | 343 | 1.91 / 3.599 | 1.337 / 34.403 |
| Next.js (cluster) | SSR | `POST /api/todos` | invalid | 2692 | 3.973 | 36.508 | 743 | 351 | 1.211 / 2.622 | 0.865 / 2.328 |
| Next.js (cluster) | SSR | `/compute?n=20000` | page | 150 | 100.23 | 259.303 | 13364 | 390 | 10.909 / 19.76 | 1191.301 / 2157.876 |
| Next.js (cluster) | SSR | `/compute?n=20000` | json | 272 | 55.334 | 129.659 | 7359 | 369 | 7.056 / 9.881 | 8.774 / 64.833 |
| Next.js (cluster) | SSR | `/compute/parallel?n=20000` | page | 113 | 134.897 | 286.292 | 17648 | 464 | 12.044 / 26.594 | 2157.876 / 3832.053 |
| Next.js (cluster) | SSR | `/compute/parallel?n=20000` | json | 199 | 90.782 | 177.994 | 10051 | 447 | 8.268 / 16.534 | 607.597 / 1423.714 |
| Next.js (cluster) | SPA (nearest) | `/` | page | 4533 | 2.52 | 20.969 | 441 | 325 | 0.738 / 1.872 | 0.643 / 1.285 |
| Next.js (cluster) | SPA (nearest) | `/` | json | 3080 | 3.973 | 31.783 | 649 | 318 | 0.768 / 1.506 | 0.724 / 1.419 |
| Next.js (cluster) | SPA (nearest) | `/todos?perPage=25` | page | 3979 | 2.952 | 25.06 | 503 | 319 | 1.285 / 2.067 | 0.848 / 1.63 |
| Next.js (cluster) | SPA (nearest) | `/todos?perPage=25` | json | 1866 | 6.918 | 41.114 | 1073 | 327 | 1.73 / 3.528 | 1.119 / 3.011 |
| Next.js (cluster) | SPA (nearest) | `/todos?perPage=100` | page | 2533 | 4.656 | 33.728 | 789 | 333 | 1.013 / 1.872 | 0.724 / 1.26 |
| Next.js (cluster) | SPA (nearest) | `/todos?perPage=100` | json | 1246 | 11.577 | 38.743 | 1602 | 332 | 1.91 / 4.564 | 1.696 / 4.053 |
| Next.js (cluster) | SPA (nearest) | `/dashboard` | page | 4190 | 2.838 | 24.087 | 477 | 319 | 1.034 / 2.108 | 0.724 / 1.364 |
| Next.js (cluster) | SPA (nearest) | `/dashboard` | json | 2869 | 3.896 | 40.308 | 697 | 334 | 1.536 / 3.973 | 0.993 / 2.622 |
| Next.js (cluster) | SPA (nearest) | `/todos/new` | page | 4162 | 2.894 | 22.698 | 480 | 301 | 0.955 / 1.987 | 0.768 / 1.598 |
| Next.js (cluster) | SPA (nearest) | `POST /api/todos` | valid | 1683 | 7.056 | 49.135 | 1168 | 331 | 1.63 / 2.674 | 1.285 / 62.315 |
| Next.js (cluster) | SPA (nearest) | `POST /api/todos` | invalid | 2737 | 4.134 | 37.983 | 731 | 334 | 1.211 / 2.108 | 0.738 / 2.194 |
| Next.js (cluster) | SPA (nearest) | `/compute?n=20000` | page | 2561 | 4.387 | 36.508 | 781 | 320 | 0.974 / 2.52 | 0.799 / 2.067 |
| Next.js (cluster) | SPA (nearest) | `/compute?n=20000` | json | 277 | 53.186 | 124.624 | 7205 | 316 | 7.056 / 9.881 | 9.128 / 85.546 |
| Next.js (cluster) | SPA (nearest) | `/compute/parallel?n=20000` | page | 2476 | 4.844 | 36.508 | 808 | 322 | 1.235 / 2.728 | 0.9 / 2.108 |
| Next.js (cluster) | SPA (nearest) | `/compute/parallel?n=20000` | json | 202 | 77.481 | 119.785 | 9881 | 388 | 8.433 / 13.835 | 208.548 / 385.311 |
| TanStack Start | SSR | `/` | page | 2543 | 5.348 | 13.564 | 510 | 123 | 0.738 / 1.63 | 0.696 / 1.419 |
| TanStack Start | SSR | `/` | json | 15199 | 0.831 | 5.348 | 92 | 115 | 0.408 / 0.783 | 0.376 / 0.918 |
| TanStack Start | SSR | `/todos?perPage=25` | page | 1311 | 11.577 | 26.073 | 1010 | 126 | 1.536 / 2.238 | 1.141 / 3.196 |
| TanStack Start | SSR | `/todos?perPage=25` | json | 4011 | 3.528 | 10.28 | 322 | 125 | 1.075 / 1.663 | 0.768 / 1.836 |
| TanStack Start | SSR | `/todos?perPage=100` | page | 592 | 26.073 | 51.12 | 2121 | 128 | 2.282 / 3.459 | 1.91 / 6.143 |
| TanStack Start | SSR | `/todos?perPage=100` | json | 1413 | 10.695 | 22.253 | 868 | 133 | 1.391 / 1.987 | 1.119 / 1.948 |
| TanStack Start | SSR | `/dashboard` | page | 3578 | 3.819 | 10.909 | 393 | 127 | 1.034 / 1.598 | 0.783 / 1.391 |
| TanStack Start | SSR | `/dashboard` | json | 10787 | 1.26 | 5.564 | 124 | 127 | 0.618 / 1.337 | 0.559 / 1.26 |
| TanStack Start | SSR | `/todos/new` | page | 2503 | 5.455 | 14.394 | 527 | 123 | 1.013 / 1.948 | 0.738 / 1.506 |
| TanStack Start | SSR | `POST /api/todos` | valid | 3857 | 3.671 | 8.268 | 254 | 103 | 1.311 / 5.675 | 0.768 / 112.876 |
| TanStack Start | SSR | `POST /api/todos` | invalid | 12663 | 1.164 | 2.894 | 86 | 114 | 0.441 / 0.936 | 0.239 / 0.571 |
| TanStack Start | SSR | `/compute?n=20000` | page | 157 | 94.449 | 192.666 | 6747 | 112 | 6.782 / 7.947 | 1315.292 / 5473.115 |
| TanStack Start | SSR | `/compute?n=20000` | json | 172 | 85.546 | 177.994 | 5950 | 120 | 6.266 / 8.268 | 584.003 / 4859.97 |
| TanStack Start | SSR | `/compute/parallel?n=20000` | page | 212 | 73.012 | 100.23 | 9401 | 235 | 5.455 / 7.638 | 5.243 / 73.012 |
| TanStack Start | SSR | `/compute/parallel?n=20000` | json | 244 | 63.562 | 80.612 | 8189 | 236 | 4.844 / 8.602 | 4.134 / 62.315 |
| TanStack Start | SPA | `/` | page | 3426 | 3.973 | 10.909 | 373 | 120 | 0.618 / 1.013 | 0.548 / 0.993 |
| TanStack Start | SPA | `/` | json | 17006 | 0.753 | 4.844 | 79 | 120 | 0.441 / 1.054 | 0.416 / 1.013 |
| TanStack Start | SPA | `/todos?perPage=25` | page | 2814 | 4.844 | 11.808 | 472 | 114 | 0.753 / 1.364 | 0.582 / 1.119 |
| TanStack Start | SPA | `/todos?perPage=25` | json | 4071 | 3.599 | 9.311 | 297 | 116 | 1.013 / 1.696 | 0.71 / 1.364 |
| TanStack Start | SPA | `/todos?perPage=100` | page | 2104 | 6.391 | 16.865 | 640 | 118 | 0.918 / 1.211 | 0.63 / 0.9 |
| TanStack Start | SPA | `/todos?perPage=100` | json | 1476 | 10.28 | 20.558 | 780 | 124 | 1.447 / 2.067 | 0.993 / 1.696 |
| TanStack Start | SPA | `/dashboard` | page | 3312 | 4.053 | 10.909 | 397 | 124 | 0.594 / 1.034 | 0.538 / 1.054 |
| TanStack Start | SPA | `/dashboard` | json | 13457 | 1.141 | 1.836 | 75 | 121 | 0.63 / 1.311 | 0.497 / 1.26 |
| TanStack Start | SPA | `/todos/new` | page | 3154 | 4.301 | 11.577 | 412 | 114 | 0.865 / 1.311 | 0.559 / 1.034 |
| TanStack Start | SPA | `POST /api/todos` | valid | 2807 | 3.973 | 33.728 | 310 | 106 | 1.391 / 100.23 | 0.936 / 4.134 |
| TanStack Start | SPA | `POST /api/todos` | invalid | 12761 | 1.141 | 2.952 | 85 | 113 | 0.571 / 1.364 | 0.369 / 0.974 |
| TanStack Start | SPA | `/compute?n=20000` | page | 2146 | 6.266 | 16.865 | 603 | 118 | 0.993 / 1.391 | 0.655 / 1.141 |
| TanStack Start | SPA | `/compute?n=20000` | json | 167 | 89.002 | 181.554 | 6104 | 126 | 6.519 / 8.602 | 740.657 / 5260.587 |
| TanStack Start | SPA | `/compute/parallel?n=20000` | page | 2085 | 6.391 | 18.255 | 645 | 143 | 0.936 / 1.337 | 0.655 / 1.187 |
| TanStack Start | SPA | `/compute/parallel?n=20000` | json | 256 | 61.094 | 79.031 | 7802 | 238 | 4.656 / 8.433 | 4.053 / 13.298 |
| TanStack Start (cluster) | SSR | `/` | page | 3044 | 3.599 | 30.548 | 657 | 256 | 0.768 / 1.506 | 0.71 / 1.598 |
| TanStack Start (cluster) | SSR | `/` | json | 16953 | 0.696 | 6.918 | 118 | 247 | 0.424 / 1.034 | 0.459 / 0.936 |
| TanStack Start (cluster) | SSR | `/todos?perPage=25` | page | 1538 | 8.268 | 43.631 | 1300 | 250 | 1.91 / 3.133 | 1.164 / 1.836 |
| TanStack Start (cluster) | SSR | `/todos?perPage=25` | json | 4132 | 3.133 | 20.558 | 483 | 249 | 1.391 / 2.674 | 0.848 / 1.696 |
| TanStack Start (cluster) | SSR | `/todos?perPage=100` | page | 757 | 18.255 | 57.57 | 2635 | 256 | 2.422 / 4.053 | 2.027 / 4.749 |
| TanStack Start (cluster) | SSR | `/todos?perPage=100` | json | 1808 | 7.488 | 31.159 | 1099 | 265 | 1.211 / 2.952 | 1.141 / 2.57 |
| TanStack Start (cluster) | SSR | `/dashboard` | page | 3407 | 3.196 | 33.067 | 587 | 261 | 1.164 / 2.151 | 0.9 / 2.108 |
| TanStack Start (cluster) | SSR | `/dashboard` | json | 13606 | 0.993 | 4.564 | 146 | 236 | 0.738 / 1.419 | 0.559 / 1.26 |
| TanStack Start (cluster) | SSR | `/todos/new` | page | 3113 | 3.459 | 31.783 | 643 | 252 | 0.936 / 2.067 | 0.831 / 1.663 |
| TanStack Start (cluster) | SSR | `POST /api/todos` | valid | 4307 | 2.894 | 16.21 | 366 | 221 | 1.364 / 2.151 | 1.034 / 61.094 |
| TanStack Start (cluster) | SSR | `POST /api/todos` | invalid | 15344 | 0.993 | 3.599 | 118 | 236 | 0.548 / 1.73 | 0.559 / 1.364 |
| TanStack Start (cluster) | SSR | `/compute?n=20000` | page | 259 | 57.57 | 143.154 | 7721 | 256 | 7.197 / 10.28 | 8.105 / 25.06 |
| TanStack Start (cluster) | SSR | `/compute?n=20000` | json | 305 | 49.135 | 110.663 | 6553 | 257 | 6.519 / 7.341 | 7.488 / 17.202 |
| TanStack Start (cluster) | SSR | `/compute/parallel?n=20000` | page | 186 | 85.546 | 154.954 | 10775 | 335 | 8.433 / 12.285 | 37.238 / 117.436 |
| TanStack Start (cluster) | SSR | `/compute/parallel?n=20000` | json | 218 | 73.012 | 102.235 | 9183 | 340 | 7.791 / 9.687 | 10.28 / 38.743 |
| TanStack Start (cluster) | SPA | `/` | page | 3649 | 2.894 | 31.783 | 547 | 239 | 0.783 / 1.536 | 0.669 / 1.63 |
| TanStack Start (cluster) | SPA | `/` | json | 17627 | 0.724 | 5.904 | 113 | 244 | 0.424 / 1.26 | 0.45 / 0.936 |
| TanStack Start (cluster) | SPA | `/todos?perPage=25` | page | 3289 | 3.196 | 35.091 | 608 | 241 | 1.235 / 2.108 | 0.753 / 1.73 |
| TanStack Start (cluster) | SPA | `/todos?perPage=25` | json | 4480 | 3.011 | 15.892 | 444 | 228 | 1.235 / 3.072 | 0.815 / 1.476 |
| TanStack Start (cluster) | SPA | `/todos?perPage=100` | page | 2426 | 4.475 | 35.792 | 824 | 241 | 0.936 / 2.108 | 0.815 / 2.194 |
| TanStack Start (cluster) | SPA | `/todos?perPage=100` | json | 1923 | 7.488 | 25.562 | 1027 | 235 | 1.364 / 2.108 | 1.187 / 2.52 |
| TanStack Start (cluster) | SPA | `/dashboard` | page | 3589 | 2.952 | 31.159 | 557 | 238 | 0.848 / 1.285 | 0.559 / 1.63 |
| TanStack Start (cluster) | SPA | `/dashboard` | json | 13705 | 1.013 | 5.564 | 145 | 240 | 0.768 / 1.567 | 0.559 / 1.187 |
| TanStack Start (cluster) | SPA | `/todos/new` | page | 3435 | 3.459 | 33.067 | 582 | 236 | 1.119 / 2.374 | 0.815 / 1.663 |
| TanStack Start (cluster) | SPA | `POST /api/todos` | valid | 4761 | 2.952 | 11.35 | 377 | 221 | 1.391 / 2.57 | 0.9 / 148.937 |
| TanStack Start (cluster) | SPA | `POST /api/todos` | invalid | 15444 | 0.936 | 3.528 | 116 | 231 | 0.582 / 1.567 | 0.45 / 1.141 |
| TanStack Start (cluster) | SPA | `/compute?n=20000` | page | 2389 | 4.475 | 36.508 | 838 | 243 | 1.075 / 2.838 | 0.955 / 3.011 |
| TanStack Start (cluster) | SPA | `/compute?n=20000` | json | 317 | 43.631 | 100.23 | 6301 | 249 | 6.391 / 7.638 | 9.311 / 31.159 |
| TanStack Start (cluster) | SPA | `/compute/parallel?n=20000` | page | 2336 | 4.564 | 37.238 | 856 | 252 | 1.164 / 2.194 | 1.097 / 3.325 |
| TanStack Start (cluster) | SPA | `/compute/parallel?n=20000` | json | 219 | 71.581 | 110.663 | 9150 | 329 | 8.105 / 10.695 | 9.687 / 26.594 |
| Nuxt | SSR | `/` | page | 1187 | 11.35 | 25.06 | 1314 | 182 | 1.211 / 4.941 | 1.097 / 6.266 |
| Nuxt | SSR | `/` | json | 32188 | 0.441 | 3.072 | 38 | 176 | 0.328 / 0.783 | 0.303 / 0.799 |
| Nuxt | SSR | `/todos?perPage=25` | page | 734 | 19.76 | 40.308 | 1978 | 145 | 1.987 / 6.022 | 1.696 / 7.791 |
| Nuxt | SSR | `/todos?perPage=25` | json | 5339 | 2.952 | 5.455 | 207 | 143 | 0.918 / 1.567 | 0.63 / 1.235 |
| Nuxt | SSR | `/todos?perPage=100` | page | 432 | 35.091 | 62.315 | 3167 | 169 | 2.782 / 6.649 | 2.422 / 12.285 |
| Nuxt | SSR | `/todos?perPage=100` | json | 1769 | 8.268 | 16.534 | 612 | 169 | 1.141 / 2.027 | 0.993 / 1.8 |
| Nuxt | SSR | `/dashboard` | page | 1049 | 13.298 | 29.362 | 1376 | 178 | 1.364 / 4.749 | 1.235 / 6.022 |
| Nuxt | SSR | `/dashboard` | json | 16782 | 0.9 | 3.325 | 67 | 174 | 0.477 / 1.187 | 0.416 / 1.075 |
| Nuxt | SSR | `/todos/new` | page | 1391 | 9.687 | 26.594 | 1079 | 174 | 1.567 / 2.282 | 0.936 / 2.067 |
| Nuxt | SSR | `POST /api/todos` | valid | 2770 | 3.973 | 58.721 | 320 | 138 | 1.364 / 5.789 | 0.753 / 15.892 |
| Nuxt | SSR | `POST /api/todos` | invalid | 11253 | 1.164 | 6.143 | 115 | 136 | 0.4 / 1.696 | 0.28 / 0.865 |
| Nuxt | SSR | `/compute?n=20000` | page | 138 | 110.663 | 148.937 | 8224 | 166 | 7.488 / 11.35 | 1878.561 / 3270.62 |
| Nuxt | SSR | `/compute?n=20000` | json | 180 | 94.449 | 134.897 | 5627 | 166 | 6.143 / 6.918 | 607.597 / 1735.5 |
| Nuxt | SSR | `/compute/parallel?n=20000` | page | 185 | 82.224 | 140.347 | 10769 | 259 | 6.266 / 10.485 | 154.954 / 342.145 |
| Nuxt | SSR | `/compute/parallel?n=20000` | json | 256 | 59.896 | 90.782 | 7795 | 258 | 4.475 / 6.266 | 3.819 / 14.976 |
| Nuxt | SPA | `/` | page | 5749 | 2.374 | 6.266 | 196 | 135 | 0.571 / 1.013 | 0.507 / 0.955 |
| Nuxt | SPA | `/` | json | 30497 | 0.45 | 3.26 | 40 | 135 | 0.334 / 0.865 | 0.297 / 0.783 |
| Nuxt | SPA | `/todos?perPage=25` | page | 5374 | 2.52 | 6.519 | 211 | 135 | 0.848 / 1.311 | 0.538 / 1.097 |
| Nuxt | SPA | `/todos?perPage=25` | json | 5382 | 3.011 | 5.455 | 206 | 136 | 0.882 / 1.63 | 0.696 / 1.337 |
| Nuxt | SPA | `/todos?perPage=100` | page | 2951 | 4.564 | 10.909 | 370 | 133 | 0.655 / 0.955 | 0.582 / 0.724 |
| Nuxt | SPA | `/todos?perPage=100` | json | 1773 | 8.268 | 16.865 | 626 | 137 | 1.187 / 1.536 | 0.955 / 1.506 |
| Nuxt | SPA | `/dashboard` | page | 5467 | 2.52 | 6.391 | 207 | 136 | 0.582 / 1.034 | 0.517 / 1.013 |
| Nuxt | SPA | `/dashboard` | json | 16982 | 0.882 | 3.391 | 67 | 136 | 0.497 / 1.034 | 0.416 / 1.235 |
| Nuxt | SPA | `/todos/new` | page | 5498 | 2.47 | 6.519 | 208 | 103 | 0.783 / 1.211 | 0.517 / 1.034 |
| Nuxt | SPA | `POST /api/todos` | valid | 3280 | 4.134 | 11.577 | 331 | 137 | 1.391 / 26.073 | 0.815 / 11.127 |
| Nuxt | SPA | `POST /api/todos` | invalid | 11300 | 1.164 | 6.143 | 114 | 137 | 0.594 / 1.987 | 0.285 / 0.882 |
| Nuxt | SPA | `/compute?n=20000` | page | 2948 | 4.656 | 11.35 | 370 | 132 | 0.918 / 1.26 | 0.63 / 0.974 |
| Nuxt | SPA | `/compute?n=20000` | json | 178 | 89.002 | 151.916 | 5665 | 132 | 6.391 / 6.918 | 619.749 / 1805.614 |
| Nuxt | SPA | `/compute/parallel?n=20000` | page | 2935 | 4.656 | 11.35 | 375 | 147 | 0.9 / 1.364 | 0.63 / 1.075 |
| Nuxt | SPA | `/compute/parallel?n=20000` | json | 263 | 58.721 | 75.962 | 7610 | 243 | 4.387 / 6.391 | 3.744 / 7.341 |
| Nuxt (cluster) | SSR | `/` | page | 1358 | 7.947 | 50.118 | 1472 | 352 | 1.285 / 5.789 | 1.164 / 5.675 |
| Nuxt (cluster) | SSR | `/` | json | 34285 | 0.369 | 2.622 | 53 | 352 | 0.334 / 0.783 | 0.297 / 0.815 |
| Nuxt (cluster) | SSR | `/todos?perPage=25` | page | 874 | 13.298 | 57.57 | 2287 | 309 | 2.47 / 6.266 | 1.948 / 6.649 |
| Nuxt (cluster) | SSR | `/todos?perPage=25` | json | 6302 | 2.328 | 6.519 | 305 | 310 | 1.235 / 1.73 | 0.738 / 1.364 |
| Nuxt (cluster) | SSR | `/todos?perPage=100` | page | 506 | 26.073 | 80.612 | 3949 | 315 | 2.952 / 6.782 | 2.57 / 10.078 |
| Nuxt (cluster) | SSR | `/todos?perPage=100` | json | 2261 | 6.519 | 21.389 | 866 | 315 | 1.187 / 1.948 | 1.013 / 1.8 |
| Nuxt (cluster) | SSR | `/dashboard` | page | 1212 | 9.311 | 50.118 | 1650 | 290 | 1.419 / 5.14 | 1.337 / 6.649 |
| Nuxt (cluster) | SSR | `/dashboard` | json | 19762 | 0.71 | 3.599 | 94 | 288 | 0.582 / 1.211 | 0.441 / 1.164 |
| Nuxt (cluster) | SSR | `/todos/new` | page | 1589 | 6.391 | 51.12 | 1260 | 354 | 1.391 / 2.027 | 0.936 / 2.238 |
| Nuxt (cluster) | SSR | `POST /api/todos` | valid | 3991 | 3.133 | 13.835 | 428 | 287 | 1.447 / 100.23 | 1.235 / 25.06 |
| Nuxt (cluster) | SSR | `POST /api/todos` | invalid | 13166 | 0.993 | 7.947 | 149 | 281 | 0.582 / 1.419 | 0.538 / 1.097 |
| Nuxt (cluster) | SSR | `/compute?n=20000` | page | 209 | 73.012 | 185.185 | 9549 | 309 | 7.791 / 11.127 | 20.155 / 134.897 |
| Nuxt (cluster) | SSR | `/compute?n=20000` | json | 333 | 46.301 | 87.257 | 6007 | 309 | 6.391 / 8.268 | 7.791 / 18.993 |
| Nuxt (cluster) | SSR | `/compute/parallel?n=20000` | page | 163 | 96.338 | 164.439 | 12258 | 391 | 9.497 / 16.865 | 670.836 / 2033.415 |
| Nuxt (cluster) | SSR | `/compute/parallel?n=20000` | json | 233 | 67.452 | 85.546 | 8592 | 400 | 7.947 / 9.687 | 10.078 / 19.372 |
| Nuxt (cluster) | SPA | `/` | page | 7276 | 1.836 | 7.791 | 275 | 242 | 0.559 / 0.974 | 0.507 / 0.936 |
| Nuxt (cluster) | SPA | `/` | json | 36740 | 0.341 | 2.674 | 49 | 273 | 0.315 / 0.724 | 0.291 / 0.815 |
| Nuxt (cluster) | SPA | `/todos?perPage=25` | page | 6802 | 1.948 | 11.127 | 294 | 245 | 1.119 / 1.91 | 0.643 / 1.164 |
| Nuxt (cluster) | SPA | `/todos?perPage=25` | json | 6557 | 2.238 | 5.904 | 296 | 289 | 1.119 / 1.73 | 0.696 / 1.337 |
| Nuxt (cluster) | SPA | `/todos?perPage=100` | page | 4023 | 3.26 | 18.62 | 497 | 243 | 0.724 / 1.054 | 0.669 / 0.974 |
| Nuxt (cluster) | SPA | `/todos?perPage=100` | json | 2306 | 6.266 | 18.993 | 848 | 282 | 1.187 / 1.536 | 1.013 / 1.696 |
| Nuxt (cluster) | SPA | `/dashboard` | page | 7028 | 1.91 | 8.268 | 285 | 245 | 0.783 / 1.285 | 0.538 / 0.993 |
| Nuxt (cluster) | SPA | `/dashboard` | json | 20150 | 0.71 | 3.671 | 92 | 278 | 0.618 / 1.097 | 0.432 / 1.164 |
| Nuxt (cluster) | SPA | `/todos/new` | page | 7036 | 1.872 | 9.311 | 285 | 208 | 0.753 / 1.311 | 0.559 / 1.034 |
| Nuxt (cluster) | SPA | `POST /api/todos` | valid | 4240 | 3.072 | 15.275 | 427 | 285 | 1.419 / 68.801 | 0.993 / 2.728 |
| Nuxt (cluster) | SPA | `POST /api/todos` | invalid | 12820 | 0.993 | 7.947 | 153 | 279 | 0.669 / 1.337 | 0.424 / 1.211 |
| Nuxt (cluster) | SPA | `/compute?n=20000` | page | 3936 | 3.391 | 16.534 | 509 | 245 | 0.799 / 1.235 | 0.643 / 0.955 |
| Nuxt (cluster) | SPA | `/compute?n=20000` | json | 345 | 41.937 | 80.612 | 5802 | 278 | 6.143 / 6.918 | 7.056 / 12.531 |
| Nuxt (cluster) | SPA | `/compute/parallel?n=20000` | page | 3975 | 3.459 | 18.255 | 503 | 247 | 0.865 / 1.211 | 0.643 / 0.918 |
| Nuxt (cluster) | SPA | `/compute/parallel?n=20000` | json | 236 | 66.13 | 83.868 | 8462 | 366 | 7.638 / 8.774 | 9.687 / 24.569 |

## Multi-core scaling

| Framework | Route | CPUs | Req/s (16 connections) | p50 ms (1 connection) |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | `/compute?n=20000` | 1 | 525 | 1.987 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 1 | 651 | 1.63 |
| Inertia.Net (JIT) | `/compute?n=20000` | 2 | 965 | 1.764 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 2 | 1023 | 1.141 |
| Inertia.Net (JIT) | `/compute?n=20000` | 4 | 1537 | 1.73 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 4 | 1648 | 0.918 |
| Next.js | `/compute?n=20000` | 1 | 147 | 6.391 |
| Next.js | `/compute/parallel?n=20000` | 1 | 129 | 7.197 |
| Next.js | `/compute?n=20000` | 2 | 146 | 6.391 |
| Next.js | `/compute/parallel?n=20000` | 2 | 215 | 4.217 |
| Next.js | `/compute?n=20000` | 4 | 148 | 6.519 |
| Next.js | `/compute/parallel?n=20000` | 4 | 278 | 3.896 |
| Next.js (cluster) | `/compute?n=20000` | 1 | 144 | 6.519 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 1 | 125 | 7.341 |
| Next.js (cluster) | `/compute?n=20000` | 2 | 258 | 6.391 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 2 | 192 | 7.197 |
| Next.js (cluster) | `/compute?n=20000` | 4 | 341 | 6.266 |
| Next.js (cluster) | `/compute/parallel?n=20000` | 4 | 306 | 7.197 |
| TanStack Start | `/compute?n=20000` | 1 | 171 | 5.675 |
| TanStack Start | `/compute/parallel?n=20000` | 1 | 145 | 6.519 |
| TanStack Start | `/compute?n=20000` | 2 | 173 | 5.675 |
| TanStack Start | `/compute/parallel?n=20000` | 2 | 255 | 3.744 |
| TanStack Start | `/compute?n=20000` | 4 | 175 | 5.675 |
| TanStack Start | `/compute/parallel?n=20000` | 4 | 332 | 3.26 |
| TanStack Start (cluster) | `/compute?n=20000` | 1 | 173 | 5.675 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 1 | 143 | 6.519 |
| TanStack Start (cluster) | `/compute?n=20000` | 2 | 322 | 5.675 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 2 | 225 | 6.519 |
| TanStack Start (cluster) | `/compute?n=20000` | 4 | 398 | 5.904 |
| TanStack Start (cluster) | `/compute/parallel?n=20000` | 4 | 352 | 6.649 |
| Nuxt | `/compute?n=20000` | 1 | 178 | 5.455 |
| Nuxt | `/compute/parallel?n=20000` | 1 | 149 | 6.649 |
| Nuxt | `/compute?n=20000` | 2 | 180 | 5.675 |
| Nuxt | `/compute/parallel?n=20000` | 2 | 259 | 3.599 |
| Nuxt | `/compute?n=20000` | 4 | 182 | 5.564 |
| Nuxt | `/compute/parallel?n=20000` | 4 | 332 | 3.133 |
| Nuxt (cluster) | `/compute?n=20000` | 1 | 181 | 5.455 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 1 | 147 | 6.519 |
| Nuxt (cluster) | `/compute?n=20000` | 2 | 343 | 5.564 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 2 | 242 | 6.519 |
| Nuxt (cluster) | `/compute?n=20000` | 4 | 421 | 5.564 |
| Nuxt (cluster) | `/compute/parallel?n=20000` | 4 | 359 | 6.519 |
