# FutureKawaHUB — Frontend (Head Office)

Frontend web application for **FutureKawaHUB**, the head-office console of the FutureKawa
stock and IoT monitoring platform. Built with [Next.js](https://nextjs.org) (App Router),
it consolidates and displays stock, batch, and warehouse condition data for FutureKawa's
three country operations (Brazil, Ecuador, Colombia). This frontend is read-only: it never
writes to country data directly, only via the head-office backend API.

## Tech stack

- [Next.js 16](https://nextjs.org) (App Router, Turbopack)
- [React 19](https://react.dev)
- [TypeScript](https://www.typescriptlang.org)
- [Tailwind CSS v4](https://tailwindcss.com)
- [Vitest](https://vitest.dev) + [Testing Library](https://testing-library.com) for unit tests

## Prerequisites

- Node.js 24 or later (`node -v` to check)
- npm (bundled with Node)
- The [FutureKawaSiege backend](../backend) running locally — required for all flows,
  including login — see that repository's own README for setup.

> **Windows note:** if you switch between WSL and native Windows for this project, do not
> share a single `node_modules` folder between the two — reinstall (`npm install`) after
> switching environments, as compiled binaries differ per OS.

## Getting started

1. Install dependencies:
```bash
   npm install
```

2. Create a `.env.local` file at the project root (this file is gitignored and must not be
   committed):
```bash
   NEXT_PUBLIC_API_BASE_URL=https://localhost:55648
```
   Adjust the URL/port to match your local backend instance if needed.

3. If the backend uses a self-signed HTTPS development certificate, trust it once per
   machine (from the backend project):
```bash
   dotnet dev-certs https --trust
```
   Without this, API calls will silently fail in the browser.

4. Start the development server:
```bash
   npm run dev
```
   Open [http://localhost:3000](http://localhost:3000).


## Environment variables

| Variable | Description | Required |
|---|---|---|
| `NEXT_PUBLIC_API_BASE_URL` | Base URL of the head-office backend API | Yes |

## API endpoints consumed

The frontend currently calls the following head-office backend routes. Request/response
shapes are defined in `lib/api/types.ts`, which is the single source of truth for these
contracts — refer to that file (or share it directly with backend developers) rather than
a copy of the shapes here.

| Endpoint | Used by |
|---|---|
| `POST /api/auth/login`, `/refresh`, `/logout`, `/me` | `lib/api/auth.ts` |
| `GET /api/batches` | `lib/api/batches.ts` — sorted oldest-first, filterable by country/warehouse, excludes shipped batches, server-side paginated (`page`/`pageSize` params, response includes `totalCount`/`totalPages`) |
| `GET /api/countries` | `lib/api/batches.ts` |
| `GET /api/warehouses` | `lib/api/batches.ts` — filterable by country |
| `GET /api/alerts` | `lib/api/alerts.ts` — open alerts only (out-of-range measurement or batch past 365-day expiry) |
| `PUT /api/alerts/:id/resolve` | `lib/api/alerts.ts` — marks an alert as resolved |

## Available scripts

| Command | Description |
|---|---|
| `npm run dev` | Start the development server (Turbopack) |
| `npm run build` | Build the app for production |
| `npm run start` | Run a production build locally |
| `npm run lint` | Run ESLint |
| `npm run test` | Run the unit test suite once |
| `npm run test:watch` | Run tests in watch mode |

## Running tests

Unit tests use Vitest and React Testing Library. API-layer tests (`batches.ts`,
`alerts.ts`, `client.ts`) mock the global `fetch`; component tests mock the API
modules directly — no backend or database connection is required either way.

```bash
npm run test
```

### Test coverage

| File | Covers |
|---|---|
| `context/AuthContext.test.tsx` | Access token kept in memory only (never persisted) |
| `context/RefreshContext.test.tsx` | Handler registration, triggering, and unregistration on unmount (backs the navbar refresh button) |
| `components/auth/LoginForm.test.tsx` | Field validation, generic error messages on failure, password visibility toggle |
| `components/layout/Navbar.test.tsx` | TODO — describe what this file covers |
| `lib/api/batches.test.ts` | Query params sent to the backend (page/pageSize defaults, country/warehouse filters), response unwrapping, error propagation |
| `lib/api/alerts.test.ts` | GET /api/alerts and PUT /api/alerts/:id/resolve request shape, response unwrapping |
| `lib/api/client.test.ts` | 401 handling: refresh-and-retry on expired token, `skipAuthRetry` guard, `ApiError` on failure |
| `components/batches/LocationFilter.test.tsx` | Country → warehouse cascading selection, reset behavior, "all countries/warehouses" options |
| `components/batches/BatchTable.test.tsx` | Empty state, row rendering, column headers |
| `components/batches/BatchRow.test.tsx` | Displayed fields (ERP reference, not internal id), status badge, navigation to batch detail |
| `components/ui/Badge.test.tsx` | French status labels, per-status color classes |
| `components/ui/PageSizeSelector.test.tsx` | Available page size options, numeric (not string) value on change |
| `components/ui/Pagination.test.tsx` | Ellipsis logic at start/middle/end of range, current page highlighting, arrow disabling on first/last page |

**Missing coverage:** `components/ui/AlertButton.tsx` has no test file yet — to be added.

End-to-end coverage of the full authentication flow (login → session persistence →
logout) requires a running backend and database, and is tracked separately from this
unit test suite.

## Updating dependencies

Dependency versions are pinned in `package-lock.json`, which **must** stay committed —
it guarantees every developer and the CI pipeline install the exact same dependency
tree.

To check for known vulnerabilities:
```bash
npm audit
```

For low-risk fixes (patch/minor bumps within the declared range):
```bash
npm audit fix
```

For a fix that requires bumping a pinned version outside its declared range (e.g. a
version pinned without `^` in `package.json`), update the version number in
`package.json` directly, then run `npm install` to regenerate the lockfile — avoid
`npm audit fix --force` blindly, as it can pull in an untested major/minor version.
Always re-run the test suite and manually verify the app after any dependency bump.

## Project structure
frontend/
├── app/                               # Next.js App Router: routes and root layout
│   ├── fifo
│   │   └── page.tsx                   # Batches FIFO listing screen
│   ├── layout.tsx                     # Root layout, font loading, AuthProvider
│   ├── page.tsx                       # "/" — login page (also the sole public entry point)
│   └── globals.css                    # Tailwind import, design tokens (colors, fonts, layout dimensions)
├── components/
│   ├── auth/
│   │   ├── LoginForm.tsx              # Login form: validation, submit, error handling
│   │   ├── LoginForm.test.tsx
│   │   └── LoginGate.tsx              # Silent-reconnect gate shown at "/"
│   ├── batches/
│   │   ├── LocationFilter.tsx         # Country → warehouse cascading select sidebar
│   │   ├── BatchTable.tsx             # Batch list table, empty state
│   │   ├── BatchRow.tsx               # Single batch row, links to quality tracking
│   │   ├── QualityTrackingButton.tsx  # Status-colored action button per row
│   │   └── grid.ts                    # Shared grid-template-columns + row styling,
│   │                                  # used by both BatchTable and BatchRow
│   ├── layout/
│   │   ├── Navbar.tsx                 # Sticky top navbar, mounted in app/layout.tsx
│   │   ├── Navbar.test.tsx
│   │   ├── UserMenu.tsx               # Avatar, role label, logout dropdown
│   │   └── UserMenu.test.tsx
│   └── ui/
│       ├── AlertButton.tsx
│       ├── Avatar.tsx                 # Generic placeholder avatar
│       ├── Button.tsx                 # Shared button component (variants: primary/alert/expired)
│       ├── Badge.tsx                  # Status badge (compliant/alert/expired)
│       ├── Badge.test.tsx
│       ├── PageSizeSelector.tsx       # Rows-per-page selector (10/15/20), reusable
│       ├── Pagination.tsx             # Page navigation bar, reusable
│       ├── RefreshButton.tsx          # Triggers router.refresh()
│       ├── RefreshButton.test.tsx
│       ├── SearchInput.tsx            # Placeholder only — search logic not yet implemented
│       └── Select.tsx                 # Generic labeled select
│
├── context/
│   └── AuthContext.tsx                # In-memory auth state, silent refresh on mount
│
├── lib/
│   └── api/
│       ├── alerts.ts                  # getUnreadAlerts / resolveAlert
│       ├── auth.ts                    # login / refresh / logout / me functions
│       ├── batches.ts                 # getBatches / getCountries / getWarehouses, getBatches is server-side paginated
│       ├── client.ts                  # Low-level fetch wrapper (ApiResponse unwrapping, 401 refresh-and-retry)
│       ├── constants.ts               # API base URL
│       └── types.ts                   # Shared API request/response types
│
├── public/
│   ├── images/                        # Photos and illustrations (e.g. login hero image)
│   └── icons/                         # Reusable SVG icons
│
├── .env.local                         # Local environment variables (gitignored, not committed)
├── .gitignore                         # List of files not committed
├── package-lock.json                  # Records the exact fully-resolved dependency tree installed
├── package.json                       # Project metadata and dependencies and version range
├── README.md                          # You are here
├── vitest.config.ts                   # Test config files
└── vitest.setup.ts

## Security notes

- The access token is kept **in memory only** (React state) — never in `localStorage`
  or `sessionStorage`. This is enforced by an automated test in `AuthContext.test.tsx`.
- Login/refresh/logout failures always surface a generic error message to the user,
  regardless of the backend's actual error detail, to avoid confirming or denying
  whether a given email is registered.

## Deployment

TODO — hosting method for the head-office frontend has not been finalized yet
(containerized alongside the head-office backend vs. a separate deployment target).
To be documented once decided with the team.