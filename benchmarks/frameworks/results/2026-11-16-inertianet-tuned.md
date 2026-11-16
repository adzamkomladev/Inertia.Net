# Inertia.Net with its caching features on: 2026-11-16

GitHub Actions run [43894743483](https://github.com/adzamkomladev/Inertia.Net/actions/runs/43894743483) of `framework-bench.yml` with `split=routes` and `--frameworks inertianet,inertianet-tuned,inertianet-aot,inertianet-aot-tuned`. Each route ran on its own runner with all four builds on it, so plain and tuned are compared on the same machine. The tuned builds (`BENCH_TUNED=1`) turn on the SSR render cache (60 s, then 5 min stale-while-revalidate), two Node SSR instances, the in-memory cache store instead of Redis, stale-while-revalidate on the dashboard cache and a 10 s cache of each todo list page. No other framework in the comparison had equivalent caching turned on. The report below is the merged `framework-bench.md`, unedited.

Each app in Docker with --cpus 2 --memory 2g; 16 keep-alive connections, 5 s warmup, 10 s measured.

Split run: each part ran on its own GitHub runner, so numbers from different parts come from different machines.
- Part 1 (--routes home): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 2 (--routes todos25): 4 CPUs, AMD EPYC 9V74 80-Core Processor
- Part 3 (--routes todos100): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 4 (--routes dashboard): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 5 (--routes new): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 6 (--routes create): 4 CPUs, Intel(R) Xeon(R) 6975P-C
- Part 7 (--routes invalid): 4 CPUs, AMD EPYC 7763 64-Core Processor
- Part 8 (--routes compute): 4 CPUs, Intel(R) Xeon(R) 6973P-C
- Part 9 (--routes parallel): 4 CPUs, Intel(R) Xeon(R) 6975P-C

"overloaded": at least one request failed or (SSR) was not server-rendered.

## Apps

| Framework | Mode | Image MB | Cold start ms | Memory at rest MB |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | SSR | 189 | 1328 | 60 |
| Inertia.Net (JIT) | SPA | 125 | 755 | 27 |
| Inertia.Net (JIT, tuned) | SSR | 189 | 1112 | 84 |
| Inertia.Net (JIT, tuned) | SPA | 125 | 642 | 23 |
| Inertia.Net (Native AOT) | SSR | 98 | 681 | 46 |
| Inertia.Net (Native AOT) | SPA | 34 | 253 | 17 |
| Inertia.Net (Native AOT, tuned) | SSR | 98 | 702 | 75 |
| Inertia.Net (Native AOT, tuned) | SPA | 34 | 244 | 16 |

## Routes

`page` is a first visit (HTML); `json` is the data a client-side navigation fetches (an Inertia visit, or the app's /api route).
`POST` rows are the new-todo form: `valid` validates and inserts a row, `invalid` fails validation on all four fields.

| Framework | Mode | Route | Response | Req/s | p50 ms | p99 ms | CPU µs per request | Peak memory MB | p50 / p99 ms at 50 req/s | p50 / p99 ms at 200 req/s |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Inertia.Net (JIT) | SSR | `/` | page | 3528 | 3.973 | 13.835 | 510 | 86 | 0.955 / 1.536 | 0.738 / 1.141 |
| Inertia.Net (JIT) | SSR | `/` | json | 8168 | 1.872 | 3.896 | 214 | 82 | 0.63 / 0.848 | 0.441 / 0.724 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=25` | page | 1561 | 8.433 | 35.091 | 1272 | 124 | 1.764 / 2.952 | 1.097 / 2.194 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=25` | json | 4248 | 3.671 | 6.782 | 388 | 116 | 1.364 / 2.282 | 0.882 / 1.567 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=100` | page | 562 | 24.569 | 61.094 | 3558 | 191 | 1.872 / 3.072 | 1.8 / 3.196 |
| Inertia.Net (JIT) | SSR | `/todos?perPage=100` | json | 2871 | 5.348 | 10.485 | 651 | 178 | 1.097 / 1.987 | 0.993 / 1.91 |
| Inertia.Net (JIT) | SSR | `/dashboard` | page | 2819 | 4.941 | 20.969 | 679 | 120 | 0.865 / 1.506 | 0.799 / 1.285 |
| Inertia.Net (JIT) | SSR | `/dashboard` | json | 9140 | 1.663 | 3.391 | 208 | 122 | 0.618 / 1.235 | 0.594 / 0.783 |
| Inertia.Net (JIT) | SSR | `/todos/new` | page | 7068 | 1.663 | 17.897 | 279 | 150 | 0.848 / 1.447 | 0.571 / 1.235 |
| Inertia.Net (JIT) | SSR | `/todos/new` | json | 22607 | 0.669 | 1.696 | 82 | 150 | 0.507 / 0.955 | 0.355 / 0.848 |
| Inertia.Net (JIT) | SSR | `POST /todos` | valid | 3764 | 3.391 | 24.569 | 503 | 105 | 0.974 / 1.536 | 0.753 / 1.235 |
| Inertia.Net (JIT) | SSR | `POST /todos` | invalid | 5623 | 2.374 | 9.687 | 317 | 83 | 0.468 / 1.097 | 0.477 / 0.799 |
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | page | 976 | 13.298 | 54.249 | 2042 | 82 | 1.987 / 3.196 | 1.8 / 2.782 |
| Inertia.Net (JIT) | SSR | `/compute?n=20000` | json | 1085 | 13.037 | 41.937 | 1834 | 84 | 1.663 / 2.782 | 1.567 / 2.622 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | page | 918 | 11.808 | 57.57 | 2176 | 86 | 1.506 / 7.341 | 1.211 / 2.108 |
| Inertia.Net (JIT) | SSR | `/compute/parallel?n=20000` | json | 1054 | 9.687 | 54.249 | 1891 | 75 | 1.097 / 5.789 | 0.974 / 1.987 |
| Inertia.Net (JIT) | SPA | `/` | page | 22858 | 0.571 | 3.26 | 63 | 42 | 0.459 / 0.63 | 0.334 / 0.487 |
| Inertia.Net (JIT) | SPA | `/` | json | 9090 | 1.696 | 3.325 | 195 | 41 | 0.559 / 0.783 | 0.424 / 0.618 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=25` | page | 4221 | 3.325 | 10.909 | 413 | 71 | 1.187 / 2.952 | 0.738 / 1.872 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=25` | json | 4955 | 3.072 | 6.519 | 326 | 68 | 1.311 / 2.151 | 0.815 / 1.211 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=100` | page | 2156 | 6.022 | 35.792 | 890 | 77 | 0.955 / 2.328 | 0.882 / 1.91 |
| Inertia.Net (JIT) | SPA | `/todos?perPage=100` | json | 3111 | 4.941 | 10.485 | 549 | 77 | 1.054 / 2.027 | 0.974 / 1.63 |
| Inertia.Net (JIT) | SPA | `/dashboard` | page | 14053 | 1.075 | 2.328 | 118 | 81 | 0.507 / 0.848 | 0.477 / 0.753 |
| Inertia.Net (JIT) | SPA | `/dashboard` | json | 9223 | 1.663 | 3.459 | 208 | 66 | 0.606 / 0.993 | 0.582 / 0.783 |
| Inertia.Net (JIT) | SPA | `/todos/new` | page | 62379 | 0.208 | 1.034 | 22 | 43 | 0.45 / 0.974 | 0.264 / 0.815 |
| Inertia.Net (JIT) | SPA | `/todos/new` | json | 23871 | 0.63 | 1.419 | 74 | 40 | 0.527 / 1.075 | 0.348 / 0.918 |
| Inertia.Net (JIT) | SPA | `POST /todos` | valid | 3477 | 3.671 | 27.126 | 558 | 75 | 0.974 / 1.447 | 0.753 / 1.285 |
| Inertia.Net (JIT) | SPA | `POST /todos` | invalid | 7527 | 1.663 | 14.394 | 262 | 52 | 0.459 / 0.831 | 0.459 / 0.831 |
| Inertia.Net (JIT) | SPA | `/compute?n=20000` | page | 1164 | 11.577 | 52.143 | 1703 | 47 | 1.598 / 3.671 | 1.447 / 2.328 |
| Inertia.Net (JIT) | SPA | `/compute?n=20000` | json | 1153 | 12.285 | 40.308 | 1727 | 46 | 1.63 / 2.728 | 1.506 / 2.47 |
| Inertia.Net (JIT) | SPA | `/compute/parallel?n=20000` | page | 1174 | 10.695 | 54.249 | 1688 | 48 | 1.034 / 1.987 | 0.918 / 6.266 |
| Inertia.Net (JIT) | SPA | `/compute/parallel?n=20000` | json | 1269 | 11.577 | 37.983 | 1565 | 44 | 1.075 / 1.987 | 0.918 / 1.8 |
| Inertia.Net (JIT, tuned) | SSR | `/` | page | 20800 | 0.669 | 2.622 | 74 | 101 | 0.392 / 0.655 | 0.334 / 0.559 |
| Inertia.Net (JIT, tuned) | SSR | `/` | json | 8906 | 1.764 | 3.325 | 199 | 97 | 0.477 / 0.724 | 0.441 / 0.783 |
| Inertia.Net (JIT, tuned) | SSR | `/todos?perPage=25` | page | 25756 | 0.548 | 1.836 | 57 | 119 | 0.487 / 0.606 | 0.234 / 0.355 |
| Inertia.Net (JIT, tuned) | SSR | `/todos?perPage=25` | json | 17565 | 0.865 | 2.238 | 107 | 129 | 0.392 / 0.682 | 0.291 / 0.955 |
| Inertia.Net (JIT, tuned) | SSR | `/todos?perPage=100` | page | 15119 | 0.955 | 2.782 | 110 | 126 | 0.392 / 0.799 | 0.348 / 0.477 |
| Inertia.Net (JIT, tuned) | SSR | `/todos?perPage=100` | json | 10235 | 1.476 | 2.894 | 178 | 120 | 0.487 / 0.783 | 0.416 / 0.696 |
| Inertia.Net (JIT, tuned) | SSR | `/dashboard` | page | 20903 | 0.669 | 2.422 | 73 | 122 | 0.341 / 0.538 | 0.309 / 0.618 |
| Inertia.Net (JIT, tuned) | SSR | `/dashboard` | json | 13102 | 1.164 | 2.57 | 144 | 121 | 0.432 / 0.643 | 0.408 / 0.669 |
| Inertia.Net (JIT, tuned) | SSR | `/todos/new` | page | 56746 | 0.239 | 1.034 | 25 | 99 | 0.408 / 1.054 | 0.264 / 0.815 |
| Inertia.Net (JIT, tuned) | SSR | `/todos/new` | json | 21838 | 0.696 | 1.764 | 86 | 102 | 0.459 / 0.865 | 0.362 / 0.882 |
| Inertia.Net (JIT, tuned) | SSR | `POST /todos` | valid | 4145 | 3.072 | 24.569 | 456 | 131 | 1.187 / 1.764 | 0.753 / 1.26 |
| Inertia.Net (JIT, tuned) | SSR | `POST /todos` | invalid | 6745 | 2.027 | 10.909 | 285 | 109 | 0.477 / 0.882 | 0.459 / 0.815 |
| Inertia.Net (JIT, tuned) | SSR | `/compute?n=20000` | page | 1207 | 11.577 | 49.135 | 1645 | 104 | 1.536 / 2.52 | 1.598 / 2.374 |
| Inertia.Net (JIT, tuned) | SSR | `/compute?n=20000` | json | 1149 | 12.782 | 39.518 | 1723 | 103 | 1.63 / 2.622 | 1.536 / 2.422 |
| Inertia.Net (JIT, tuned) | SSR | `/compute/parallel?n=20000` | page | 1146 | 9.881 | 54.249 | 1735 | 105 | 1.034 / 6.143 | 0.9 / 5.564 |
| Inertia.Net (JIT, tuned) | SSR | `/compute/parallel?n=20000` | json | 1190 | 11.577 | 43.631 | 1664 | 103 | 1.235 / 2.194 | 0.955 / 2.328 |
| Inertia.Net (JIT, tuned) | SPA | `/` | page | 23313 | 0.571 | 3.26 | 62 | 38 | 0.369 / 0.548 | 0.321 / 0.527 |
| Inertia.Net (JIT, tuned) | SPA | `/` | json | 12504 | 1.211 | 2.57 | 142 | 36 | 0.468 / 0.768 | 0.432 / 0.594 |
| Inertia.Net (JIT, tuned) | SPA | `/todos?perPage=25` | page | 29818 | 0.432 | 2.422 | 49 | 58 | 0.309 / 1.164 | 0.221 / 0.341 |
| Inertia.Net (JIT, tuned) | SPA | `/todos?perPage=25` | json | 16167 | 0.936 | 2.108 | 108 | 67 | 0.369 / 1.013 | 0.285 / 0.643 |
| Inertia.Net (JIT, tuned) | SPA | `/todos?perPage=100` | page | 18474 | 0.768 | 2.622 | 86 | 62 | 0.341 / 0.71 | 0.328 / 0.582 |
| Inertia.Net (JIT, tuned) | SPA | `/todos?perPage=100` | json | 10693 | 1.391 | 3.072 | 178 | 67 | 0.432 / 1.075 | 0.392 / 0.682 |
| Inertia.Net (JIT, tuned) | SPA | `/dashboard` | page | 23084 | 0.571 | 3.26 | 64 | 61 | 0.328 / 0.507 | 0.309 / 0.384 |
| Inertia.Net (JIT, tuned) | SPA | `/dashboard` | json | 12957 | 1.187 | 2.52 | 144 | 61 | 0.432 / 0.955 | 0.416 / 0.548 |
| Inertia.Net (JIT, tuned) | SPA | `/todos/new` | page | 63280 | 0.204 | 0.993 | 21 | 38 | 0.369 / 0.865 | 0.264 / 0.753 |
| Inertia.Net (JIT, tuned) | SPA | `/todos/new` | json | 28235 | 0.517 | 1.285 | 65 | 37 | 0.487 / 0.882 | 0.348 / 0.918 |
| Inertia.Net (JIT, tuned) | SPA | `POST /todos` | valid | 3635 | 3.528 | 28.787 | 530 | 70 | 0.955 / 1.476 | 0.753 / 1.26 |
| Inertia.Net (JIT, tuned) | SPA | `POST /todos` | invalid | 6332 | 1.987 | 21.389 | 307 | 49 | 0.477 / 0.848 | 0.459 / 0.768 |
| Inertia.Net (JIT, tuned) | SPA | `/compute?n=20000` | page | 1198 | 11.577 | 51.12 | 1663 | 44 | 1.476 / 2.952 | 1.364 / 2.067 |
| Inertia.Net (JIT, tuned) | SPA | `/compute?n=20000` | json | 1172 | 12.531 | 39.518 | 1697 | 41 | 1.598 / 2.622 | 1.598 / 2.374 |
| Inertia.Net (JIT, tuned) | SPA | `/compute/parallel?n=20000` | page | 1299 | 10.695 | 49.135 | 1490 | 49 | 1.034 / 1.948 | 0.936 / 1.73 |
| Inertia.Net (JIT, tuned) | SPA | `/compute/parallel?n=20000` | json | 1311 | 10.909 | 38.743 | 1509 | 44 | 1.141 / 2.151 | 0.936 / 6.143 |
| Inertia.Net (Native AOT) | SSR | `/` | page | 4378 | 3.133 | 16.865 | 448 | 63 | 0.815 / 1.337 | 0.669 / 0.974 |
| Inertia.Net (Native AOT) | SSR | `/` | json | 11765 | 1.311 | 3.072 | 160 | 66 | 0.497 / 0.753 | 0.45 / 0.738 |
| Inertia.Net (Native AOT) | SSR | `/todos?perPage=25` | page | 3293 | 4.564 | 9.497 | 569 | 88 | 1.054 / 1.506 | 0.848 / 1.211 |
| Inertia.Net (Native AOT) | SSR | `/todos?perPage=25` | json | 7276 | 2.067 | 4.564 | 218 | 84 | 0.783 / 1.097 | 0.559 / 1.211 |
| Inertia.Net (Native AOT) | SSR | `/todos?perPage=100` | page | 1033 | 14.394 | 27.669 | 1861 | 157 | 1.764 / 2.374 | 1.663 / 2.728 |
| Inertia.Net (Native AOT) | SSR | `/todos?perPage=100` | json | 4130 | 3.744 | 7.341 | 412 | 138 | 1.054 / 1.337 | 0.848 / 1.311 |
| Inertia.Net (Native AOT) | SSR | `/dashboard` | page | 3586 | 3.819 | 22.253 | 532 | 78 | 0.9 / 1.235 | 0.831 / 1.235 |
| Inertia.Net (Native AOT) | SSR | `/dashboard` | json | 8910 | 1.73 | 3.325 | 211 | 90 | 0.63 / 1.311 | 0.618 / 0.783 |
| Inertia.Net (Native AOT) | SSR | `/todos/new` | page | 7773 | 1.8 | 7.791 | 254 | 125 | 1.013 / 1.567 | 0.606 / 1.235 |
| Inertia.Net (Native AOT) | SSR | `/todos/new` | json | 27313 | 0.548 | 2.027 | 69 | 129 | 0.643 / 1.075 | 0.362 / 0.9 |
| Inertia.Net (Native AOT) | SSR | `POST /todos` | valid | 8289 | 1.8 | 4.053 | 180 | 50 | 0.955 / 1.536 | 0.669 / 1.119 |
| Inertia.Net (Native AOT) | SSR | `POST /todos` | invalid | 7136 | 1.987 | 6.391 | 265 | 50 | 0.507 / 0.865 | 0.497 / 0.669 |
| Inertia.Net (Native AOT) | SSR | `/compute?n=20000` | page | 882 | 14.112 | 58.721 | 2265 | 68 | 2.108 / 7.791 | 1.987 / 3.072 |
| Inertia.Net (Native AOT) | SSR | `/compute?n=20000` | json | 965 | 14.976 | 43.631 | 2057 | 57 | 1.8 / 2.838 | 1.696 / 2.782 |
| Inertia.Net (Native AOT) | SSR | `/compute/parallel?n=20000` | page | 778 | 12.531 | 64.833 | 2564 | 71 | 1.63 / 7.791 | 1.285 / 2.238 |
| Inertia.Net (Native AOT) | SSR | `/compute/parallel?n=20000` | json | 967 | 13.298 | 56.441 | 2045 | 58 | 1.419 / 6.782 | 1.119 / 2.238 |
| Inertia.Net (Native AOT) | SPA | `/` | page | 20630 | 0.682 | 2.622 | 70 | 19 | 0.408 / 0.643 | 0.362 / 0.45 |
| Inertia.Net (Native AOT) | SPA | `/` | json | 11954 | 1.285 | 3.011 | 158 | 23 | 0.538 / 0.753 | 0.468 / 0.682 |
| Inertia.Net (Native AOT) | SPA | `/todos?perPage=25` | page | 8634 | 1.764 | 4.053 | 154 | 25 | 0.643 / 0.831 | 0.527 / 0.815 |
| Inertia.Net (Native AOT) | SPA | `/todos?perPage=25` | json | 7325 | 2.027 | 4.844 | 226 | 25 | 1.013 / 1.91 | 0.63 / 0.936 |
| Inertia.Net (Native AOT) | SPA | `/todos?perPage=100` | page | 4763 | 3.196 | 6.782 | 314 | 35 | 0.848 / 1.097 | 0.753 / 0.955 |
| Inertia.Net (Native AOT) | SPA | `/todos?perPage=100` | json | 4220 | 3.599 | 7.791 | 414 | 26 | 0.974 / 1.285 | 0.882 / 1.075 |
| Inertia.Net (Native AOT) | SPA | `/dashboard` | page | 13608 | 1.141 | 2.282 | 114 | 41 | 0.548 / 0.724 | 0.507 / 0.682 |
| Inertia.Net (Native AOT) | SPA | `/dashboard` | json | 9021 | 1.73 | 3.196 | 207 | 46 | 0.618 / 0.799 | 0.606 / 0.753 |
| Inertia.Net (Native AOT) | SPA | `/todos/new` | page | 57912 | 0.239 | 1.034 | 23 | 24 | 0.408 / 0.918 | 0.285 / 0.882 |
| Inertia.Net (Native AOT) | SPA | `/todos/new` | json | 28783 | 0.517 | 1.337 | 66 | 21 | 0.468 / 0.9 | 0.348 / 0.9 |
| Inertia.Net (Native AOT) | SPA | `POST /todos` | valid | 8556 | 1.8 | 3.459 | 176 | 25 | 0.815 / 1.211 | 0.669 / 1.164 |
| Inertia.Net (Native AOT) | SPA | `POST /todos` | invalid | 8393 | 1.73 | 7.197 | 234 | 23 | 0.527 / 0.918 | 0.487 / 0.63 |
| Inertia.Net (Native AOT) | SPA | `/compute?n=20000` | page | 976 | 14.394 | 54.249 | 2044 | 21 | 1.8 / 3.599 | 1.696 / 2.952 |
| Inertia.Net (Native AOT) | SPA | `/compute?n=20000` | json | 1020 | 14.112 | 41.114 | 1940 | 32 | 1.987 / 3.391 | 1.663 / 2.782 |
| Inertia.Net (Native AOT) | SPA | `/compute/parallel?n=20000` | page | 1065 | 12.782 | 55.334 | 1864 | 23 | 1.311 / 7.056 | 1.013 / 2.108 |
| Inertia.Net (Native AOT) | SPA | `/compute/parallel?n=20000` | json | 860 | 10.485 | 61.094 | 2323 | 31 | 1.26 / 7.197 | 1.054 / 1.948 |
| Inertia.Net (Native AOT, tuned) | SSR | `/` | page | 17952 | 0.815 | 2.374 | 87 | 76 | 0.571 / 0.738 | 0.362 / 0.487 |
| Inertia.Net (Native AOT, tuned) | SSR | `/` | json | 10980 | 1.391 | 3.011 | 168 | 82 | 0.618 / 0.815 | 0.45 / 0.571 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos?perPage=25` | page | 23443 | 0.63 | 1.764 | 66 | 75 | 0.477 / 0.594 | 0.258 / 0.63 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos?perPage=25` | json | 16047 | 0.936 | 2.282 | 118 | 85 | 0.392 / 0.594 | 0.297 / 0.753 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos?perPage=100` | page | 13745 | 1.075 | 2.782 | 123 | 77 | 0.45 / 0.618 | 0.369 / 0.527 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos?perPage=100` | json | 10480 | 1.447 | 3.133 | 186 | 86 | 0.497 / 0.655 | 0.416 / 0.559 |
| Inertia.Net (Native AOT, tuned) | SSR | `/dashboard` | page | 18054 | 0.815 | 2.328 | 87 | 75 | 0.376 / 0.497 | 0.328 / 0.45 |
| Inertia.Net (Native AOT, tuned) | SSR | `/dashboard` | json | 11353 | 1.337 | 2.782 | 165 | 81 | 0.459 / 1.187 | 0.432 / 0.538 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos/new` | page | 49923 | 0.285 | 1.034 | 28 | 75 | 0.328 / 0.815 | 0.285 / 0.738 |
| Inertia.Net (Native AOT, tuned) | SSR | `/todos/new` | json | 28107 | 0.538 | 1.567 | 67 | 85 | 0.424 / 0.918 | 0.348 / 0.936 |
| Inertia.Net (Native AOT, tuned) | SSR | `POST /todos` | valid | 8387 | 1.8 | 3.819 | 178 | 78 | 0.918 / 1.337 | 0.669 / 1.235 |
| Inertia.Net (Native AOT, tuned) | SSR | `POST /todos` | invalid | 8378 | 1.73 | 7.341 | 234 | 78 | 0.517 / 0.738 | 0.487 / 0.63 |
| Inertia.Net (Native AOT, tuned) | SSR | `/compute?n=20000` | page | 971 | 13.835 | 56.441 | 2035 | 85 | 1.764 / 5.348 | 1.663 / 2.622 |
| Inertia.Net (Native AOT, tuned) | SSR | `/compute?n=20000` | json | 961 | 14.976 | 43.631 | 2080 | 81 | 1.8 / 2.894 | 1.8 / 2.728 |
| Inertia.Net (Native AOT, tuned) | SSR | `/compute/parallel?n=20000` | page | 908 | 10.28 | 62.315 | 2199 | 97 | 1.075 / 6.782 | 1.013 / 1.987 |
| Inertia.Net (Native AOT, tuned) | SSR | `/compute/parallel?n=20000` | json | 1087 | 13.298 | 41.114 | 1825 | 81 | 1.364 / 2.422 | 1.054 / 1.987 |
| Inertia.Net (Native AOT, tuned) | SPA | `/` | page | 20711 | 0.71 | 2.151 | 70 | 18 | 0.45 / 0.63 | 0.376 / 0.507 |
| Inertia.Net (Native AOT, tuned) | SPA | `/` | json | 11828 | 1.285 | 2.838 | 160 | 22 | 0.606 / 0.831 | 0.459 / 0.559 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos?perPage=25` | page | 26758 | 0.538 | 1.63 | 55 | 18 | 0.334 / 0.497 | 0.244 / 0.376 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos?perPage=25` | json | 16335 | 0.918 | 2.151 | 116 | 28 | 0.392 / 0.538 | 0.291 / 0.424 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos?perPage=100` | page | 16822 | 0.882 | 2.47 | 97 | 19 | 0.369 / 0.517 | 0.334 / 0.527 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos?perPage=100` | json | 10465 | 1.447 | 3.26 | 187 | 29 | 0.459 / 0.606 | 0.408 / 0.594 |
| Inertia.Net (Native AOT, tuned) | SPA | `/dashboard` | page | 21087 | 0.682 | 2.238 | 70 | 18 | 0.376 / 0.477 | 0.334 / 0.424 |
| Inertia.Net (Native AOT, tuned) | SPA | `/dashboard` | json | 12154 | 1.26 | 2.57 | 158 | 22 | 0.459 / 0.582 | 0.441 / 0.538 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos/new` | page | 58268 | 0.244 | 0.918 | 23 | 17 | 0.355 / 0.9 | 0.28 / 0.783 |
| Inertia.Net (Native AOT, tuned) | SPA | `/todos/new` | json | 28956 | 0.517 | 1.536 | 65 | 22 | 0.477 / 0.974 | 0.348 / 0.882 |
| Inertia.Net (Native AOT, tuned) | SPA | `POST /todos` | valid | 8662 | 1.764 | 3.528 | 180 | 21 | 0.848 / 1.311 | 0.682 / 1.235 |
| Inertia.Net (Native AOT, tuned) | SPA | `POST /todos` | invalid | 8579 | 1.73 | 4.387 | 229 | 22 | 0.517 / 0.831 | 0.497 / 0.63 |
| Inertia.Net (Native AOT, tuned) | SPA | `/compute?n=20000` | page | 983 | 14.112 | 51.12 | 2025 | 20 | 1.8 / 3.072 | 1.63 / 2.674 |
| Inertia.Net (Native AOT, tuned) | SPA | `/compute?n=20000` | json | 1000 | 14.394 | 40.308 | 1985 | 30 | 1.8 / 2.838 | 1.696 / 2.674 |
| Inertia.Net (Native AOT, tuned) | SPA | `/compute/parallel?n=20000` | page | 1107 | 12.782 | 44.503 | 1790 | 20 | 1.337 / 2.622 | 1.054 / 2.151 |
| Inertia.Net (Native AOT, tuned) | SPA | `/compute/parallel?n=20000` | json | 888 | 12.044 | 59.896 | 2242 | 21 | 1.337 / 2.838 | 1.097 / 2.067 |

## Multi-core scaling

| Framework | Route | CPUs | Req/s (16 connections) | p50 ms (1 connection) |
|---|---|---:|---:|---:|
| Inertia.Net (JIT) | `/compute?n=20000` | 1 | 640 | 1.8 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 1 | 726 | 0.936 |
| Inertia.Net (JIT) | `/compute?n=20000` | 2 | 1121 | 1.598 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 2 | 1151 | 0.865 |
| Inertia.Net (JIT) | `/compute?n=20000` | 4 | 2036 | 1.447 |
| Inertia.Net (JIT) | `/compute/parallel?n=20000` | 4 | 2006 | 0.71 |
| Inertia.Net (JIT, tuned) | `/compute?n=20000` | 1 | 636 | 1.836 |
| Inertia.Net (JIT, tuned) | `/compute/parallel?n=20000` | 1 | 686 | 0.974 |
| Inertia.Net (JIT, tuned) | `/compute?n=20000` | 2 | 1195 | 1.419 |
| Inertia.Net (JIT, tuned) | `/compute/parallel?n=20000` | 2 | 1197 | 0.799 |
| Inertia.Net (JIT, tuned) | `/compute?n=20000` | 4 | 2086 | 1.447 |
| Inertia.Net (JIT, tuned) | `/compute/parallel?n=20000` | 4 | 2102 | 0.682 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 1 | 624 | 1.73 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 1 | 601 | 1.034 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 2 | 1019 | 1.63 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 2 | 919 | 0.9 |
| Inertia.Net (Native AOT) | `/compute?n=20000` | 4 | 1591 | 1.63 |
| Inertia.Net (Native AOT) | `/compute/parallel?n=20000` | 4 | 1699 | 0.799 |
| Inertia.Net (Native AOT, tuned) | `/compute?n=20000` | 1 | 625 | 1.696 |
| Inertia.Net (Native AOT, tuned) | `/compute/parallel?n=20000` | 1 | 579 | 1.054 |
| Inertia.Net (Native AOT, tuned) | `/compute?n=20000` | 2 | 987 | 1.63 |
| Inertia.Net (Native AOT, tuned) | `/compute/parallel?n=20000` | 2 | 978 | 0.882 |
| Inertia.Net (Native AOT, tuned) | `/compute?n=20000` | 4 | 1736 | 1.663 |
| Inertia.Net (Native AOT, tuned) | `/compute/parallel?n=20000` | 4 | 1714 | 0.783 |
