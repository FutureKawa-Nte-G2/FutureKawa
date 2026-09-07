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
import { registerAuthRefreshHandler } from "../lib/api/client";
import type { LoginRequest, UserResponse } from "../lib/api/types";

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
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [user, setUser] = useState<UserResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  // Attempt silent reconnection on initial mount (app load / page refresh).
  useEffect(() => {
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

  // Lets client.ts recover from an expired access token mid-session
  useEffect(() => {
    registerAuthRefreshHandler(async () => {
      try {
        const response = await apiRefresh();
        setAccessToken(response.accessToken);
        setUser(response.user);
        return response.accessToken;
      } catch {
        setAccessToken(null);
        setUser(null);
        return null;
      }
    });

    return () => registerAuthRefreshHandler(null);
  }, []);

  const loginUser = useCallback(async (credentials: LoginRequest) => {
    const response = await apiLogin(credentials);
    setAccessToken(response.accessToken);
    setUser(response.user);
  }, []);

  const logoutUser = useCallback(async () => {
    try {
      await apiLogout();
    } finally {
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