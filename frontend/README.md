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
- The [FutureKawaSiege backend](../backend) running locally for any authenticated flow
  (login, dashboard data) — see that repository's own README for setup

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

Unit tests use Vitest and React Testing Library, and run entirely against mocked API
calls — no backend or database connection is required.

```bash
npm run test
```

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
├── app/                      # Next.js App Router: routes and root layout
│   ├── layout.tsx            # Root layout, font loading, AuthProvider
│   ├── page.tsx               # "/" — login page (also the sole public entry point)
│   └── globals.css            # Tailwind import, design tokens (colors, fonts)
├── components/
│   ├── auth/
│   │   ├── LoginForm.tsx      # Login form: validation, submit, error handling
│   │   ├── LoginForm.test.tsx
│   │   └── LoginGate.tsx      # Silent-reconnect gate shown at "/"
│   └── ui/
│       └── Button.tsx         # Shared button component (variants)
├── context/
│   ├── AuthContext.tsx        # In-memory auth state, silent refresh on mount
│   └── AuthContext.test.tsx
├── lib/
│   └── api/
│       ├── client.ts          # Low-level fetch wrapper (ApiResponse unwrapping)
│       ├── constants.ts       # API base URL
│       ├── auth.ts            # login / refresh / logout / me functions
│       └── types.ts           # Shared API request/response types
├── public/
│   ├── images/                # Photos and illustrations (e.g. login hero image)
│   └── icons/                 # Reusable SVG icons
├── .env.local                 # Local environment variables (gitignored, not committed)
├── .gitignore                 # List of files not committed
├── package-lock.json          # Records the exact fully-resolved dependency tree installed
├── package.json               # Project metadata and dependencies and version range
├── README.md                  # You are here
├── vitest.config.ts           # Test config files
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