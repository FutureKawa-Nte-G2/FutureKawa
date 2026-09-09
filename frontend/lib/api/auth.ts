import { apiRequest } from "./client";
import type { LoginRequest, LoginResponse, UserResponse } from "./types";

// POST /api/auth/login
// Authenticates with email/password, returns the access token + user info.
// The refresh token cookie is set automatically by the browser (httpOnly).
export function login(credentials: LoginRequest): Promise<LoginResponse> {
  return apiRequest<LoginResponse>("/api/auth/login", {
    method: "POST",
    body: credentials,
    skipAuthRetry: true
  });
}

// POST /api/auth/refresh
// Exchanges the httpOnly refresh cookie for a new access token.
// No body needed: the cookie is sent automatically (credentials: 'include').
export function refresh(): Promise<LoginResponse> {
  return apiRequest<LoginResponse>("/api/auth/refresh", {
    method: "POST",
    skipAuthRetry: true
  });
}

// POST /api/auth/logout
// Revokes the refresh token server-side and clears the cookie.
export function logout(): Promise<void> {
  return apiRequest<void>("/api/auth/logout", {
    method: "POST",
    skipAuthRetry: true
  });
}

// GET /api/auth/me
// Returns the current user's info. Requires a valid access token.
export function me(accessToken: string): Promise<UserResponse> {
  return apiRequest<UserResponse>("/api/auth/me", {
    method: "GET",
    accessToken,
  });
}