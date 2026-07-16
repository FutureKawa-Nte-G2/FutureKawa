import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { AuthProvider, useAuth } from "./AuthContext";

const mockRefresh = vi.fn();
const mockLogin = vi.fn();
const mockLogout = vi.fn();

vi.mock("../lib/api/auth", () => ({
  refresh: () => mockRefresh(),
  login: (credentials: unknown) => mockLogin(credentials),
  logout: () => mockLogout(),
}));

const FAKE_USER = {
  id: "1",
  email: "test@futurekawa.com",
  role: "Admin",
  country: "FR",
  warehouseId: null,
};

// Small probe component to read the context's state in tests.
function AuthProbe() {
  const { isAuthenticated, isLoading, user } = useAuth();
  return (
    <div>
      <span data-testid="loading">{String(isLoading)}</span>
      <span data-testid="authenticated">{String(isAuthenticated)}</span>
      <span data-testid="email">{user?.email ?? "none"}</span>
    </div>
  );
}

describe("AuthProvider", () => {
  beforeEach(() => {
    mockRefresh.mockReset();
    mockLogin.mockReset();
    mockLogout.mockReset();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("silently reconnects on mount when the refresh cookie is valid", async () => {
    mockRefresh.mockResolvedValueOnce({
      accessToken: "fake-token",
      refreshToken: "fake-refresh-token",
      user: FAKE_USER,
    });

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>
    );

    expect(screen.getByTestId("loading").textContent).toBe("true");

    await waitFor(() => {
      expect(screen.getByTestId("loading").textContent).toBe("false");
    });

    expect(screen.getByTestId("authenticated").textContent).toBe("true");
    expect(screen.getByTestId("email").textContent).toBe("test@futurekawa.com");
  });

  it("stays logged out when there is no valid refresh cookie", async () => {
    mockRefresh.mockRejectedValueOnce(new Error("No refresh token"));

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>
    );

    await waitFor(() => {
      expect(screen.getByTestId("loading").textContent).toBe("false");
    });

    expect(screen.getByTestId("authenticated").textContent).toBe("false");
  });

  it("never writes the access token to localStorage or sessionStorage", async () => {
    mockRefresh.mockResolvedValueOnce({
      accessToken: "fake-token",
      refreshToken: "fake-refresh-token",
      user: FAKE_USER,
    });

    render(
      <AuthProvider>
        <AuthProbe />
      </AuthProvider>
    );

    await waitFor(() => {
      expect(screen.getByTestId("loading").textContent).toBe("false");
    });

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});