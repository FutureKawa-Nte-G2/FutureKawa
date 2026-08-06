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
}

// Low-level request helper. Always sends cookies (needed for the refresh cookie).
// Does NOT handle 401 retry/refresh logic yet — that comes in a later step.
export async function apiRequest<T>(
  path: string,
  options: RequestOptions = {}
): Promise<T> {
  const { method = "GET", body, accessToken } = options;

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };

  if (accessToken) {
    headers["Authorization"] = `Bearer ${accessToken}`;
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    method,
    headers,
    credentials: "include", // sends/receives the httpOnly refresh cookie
    body: body ? JSON.stringify(body) : undefined,
  });

  const payload: ApiResponse<T> = await response.json();

  if (!response.ok || !payload.success) {
    throw new ApiError(
      payload.message ?? "Request failed",
      response.status,
      payload.errors
    );
  }

  return payload.data as T;
}