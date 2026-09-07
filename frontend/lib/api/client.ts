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

// Called by AuthContext once, on mount, to hand us a way to refresh the access token
type AuthRefreshHandler = () => Promise<string | null>;
let authRefreshHandler: AuthRefreshHandler | null = null;

export function registerAuthRefreshHandler(handler: AuthRefreshHandler | null) {
  authRefreshHandler = handler;
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
  });
}

async function parseResponse<T>(response: Response): Promise<T> {
  const rawBody = await response.text();
  const payload: ApiResponse<T> | null = rawBody ? JSON.parse(rawBody) : null;

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
    const newToken = await authRefreshHandler();
    if (newToken) {
      const retryResponse = await doFetch(path, method, body, newToken);
      return parseResponse<T>(retryResponse);
    }
  }

  return parseResponse<T>(response);
}