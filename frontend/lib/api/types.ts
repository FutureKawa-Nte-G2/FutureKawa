// Generic envelope returned by every backend route
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: string[] | null;
}

// Body sent to POST /api/auth/login
export interface LoginRequest {
  email: string;
  password: string;
}

// User info returned by /login, /refresh and /me
export interface UserResponse {
  id: string;
  email: string;
  role: string;
  country: string;
  warehouseId: string | null;
}

// Data returned by /login and /refresh
// Note: refreshToken is typed here for accuracy (the backend does return it),
// but the frontend must NEVER read or store it — the httpOnly cookie handles it.
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  user: UserResponse;
}