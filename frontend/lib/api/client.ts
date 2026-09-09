import type { ApiResponse } from "./types";
import { API_BASE_URL } from "./constants";


// Thrown when the backend responds with success: false, or a non-2xx status
export class ApiError extends Error {
  status: number;
  errors: string[] | null;

  constructor(message: string, status: number, errors: string[] | null = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.errors = errors;
  }
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  accessToken?: string | null;
  skipAuthRetry?: boolean; // Internal use only: set by auth.ts on login/refresh/logout calls,
}

// A backend that accepts the connection but never answers — starting up,
// paused, deadlocked — leaves fetch pending forever, and the page sits on its
// loading state with no way out. A deadline turns that into an error the caller
// can show.
const REQUEST_TIMEOUT_MS = 10_000;

// Called by AuthContext once, on mount, to hand us a way to refresh the access token
type AuthRefreshHandler = () => Promise<string | null>;
let authRefreshHandler: AuthRefreshHandler | null = null;

// The refresh currently running, if any. Head office rotates the refresh token
// on every use: ValidateAndRotateAsync revokes the old one before issuing a
// new one. A page that fires several authenticated calls at once — /fifo asks
// for batches, countries and alerts on mount — would send as many refreshes,
// and only the first would find a token still valid. The others would be
// rejected, AuthContext would clear the session on their behalf, and the user
// would be signed out at the very moment the refresh was meant to keep them in.
//
// Sharing one in-flight promise makes the concurrent callers wait for the same
// rotation instead of racing it.
let refreshInFlight: Promise<string | null> | null = null;

export function registerAuthRefreshHandler(handler: AuthRefreshHandler | null) {
  authRefreshHandler = handler;
  refreshInFlight = null;
}

async function refreshOnce(): Promise<string | null> {
  if (!authRefreshHandler) return null;
  refreshInFlight ??= authRefreshHandler().finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

async function doFetch(
  path: string,
  method: string,
  body: unknown,
  accessToken?: string | null
): Promise<Response> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  if (accessToken) {
    headers["Authorization"] = `Bearer ${accessToken}`;
  }
  return fetch(`${API_BASE_URL}${path}`, {
    method,
    headers,
    credentials: "include", // sends/receives the httpOnly refresh cookie
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
}

async function parseResponse<T>(response: Response): Promise<T> {
  const rawBody = await response.text();

  // Not every answer carries our envelope. An empty body is already handled —
  // that is the 401 the JWT middleware returns — but a non-JSON body is not:
  // the head office middleware guarding POST /api/alerts replies in plain text
  // ("Unauthorized: invalid or missing X-Api-Key."), a proxy 502 replies in
  // HTML, and ASP.NET's developer page too. Parsing those outside a try threw a
  // SyntaxError instead of an ApiError, and the HTTP status was lost on the way.
  let payload: ApiResponse<T> | null = null;
  if (rawBody) {
    try {
      payload = JSON.parse(rawBody) as ApiResponse<T>;
    } catch {
      payload = null;
    }
  }

  if (!response.ok || !payload?.success) {
    throw new ApiError(
      payload?.message ?? `Request failed with status ${response.status}`,
      response.status,
      payload?.errors ?? null
    );
  }

  return payload.data as T;
}

// Low-level request helper. Always sends cookies (needed for the refresh cookie).
export async function apiRequest<T>(
  path: string,
  options: RequestOptions = {}
): Promise<T> {
  const { method = "GET", body, accessToken, skipAuthRetry = false } = options;

  const response = await doFetch(path, method, body, accessToken);

  if (response.status === 401 && !skipAuthRetry && authRefreshHandler) {
    const newToken = await refreshOnce();
    if (newToken) {
      const retryResponse = await doFetch(path, method, body, newToken);
      return parseResponse<T>(retryResponse);
    }
  }

  return parseResponse<T>(response);
}