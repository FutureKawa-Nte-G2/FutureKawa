"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { login as apiLogin, logout as apiLogout, refresh as apiRefresh } from "../lib/api/auth";
import type { LoginRequest, UserResponse } from "../lib/api/types";
import mockUser from "../lib/api/mocks/user.json";

const USE_MOCKS = process.env.NEXT_PUBLIC_USE_MOCKS === "true";

interface AuthContextValue {
  accessToken: string | null;
  user: UserResponse | null;
  isLoading: boolean; // true while attempting silent reconnection on mount
  isAuthenticated: boolean;
  loginUser: (credentials: LoginRequest) => Promise<void>;
  logoutUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  // In mock mode, the app should already act as an authenticated HQ user
  // from the very first render — no effect needed to reach that state.
  const [accessToken, setAccessToken] = useState<string | null>(
    USE_MOCKS ? "mock-access-token" : null
  );
  const [user, setUser] = useState<UserResponse | null>(
    USE_MOCKS ? (mockUser as UserResponse) : null
  );
  const [isLoading, setIsLoading] = useState(!USE_MOCKS);

  // Attempt silent reconnection on initial mount (app load / page refresh).
  // In mock mode, this effect has nothing to do: initial state above already
  // represents an already-authenticated HQ user.
  useEffect(() => {
    if (USE_MOCKS) return;

    let cancelled = false;

    apiRefresh()
      .then((response) => {
        if (cancelled) return;
        setAccessToken(response.accessToken);
        setUser(response.user);
      })
      .catch(() => {
        // No valid refresh cookie: user is simply not logged in, this is expected.
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const loginUser = useCallback(async (credentials: LoginRequest) => {
    const response = await apiLogin(credentials);
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, []);

  const logoutUser = useCallback(async () => {
    if (USE_MOCKS) {
      setAccessToken(null);
      setUser(null);
      return;
    }
    try {
      await apiLogout();
    } finally {
      // Always clear local state, even if the server call fails
      setAccessToken(null);
      setUser(null);
    }
  }, []);

  const value: AuthContextValue = {
    accessToken,
    user,
    isLoading,
    isAuthenticated: accessToken !== null,
    loginUser,
    logoutUser,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}