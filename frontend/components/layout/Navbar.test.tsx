import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Navbar } from "./Navbar";
import { RefreshProvider } from "@/context/RefreshContext";

const mockUseAuth = vi.fn();

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn() }),
}));

vi.mock("@/lib/api/alerts", () => ({
  getUnreadAlerts: () => Promise.resolve([]),
  markAlertAsRead: () => Promise.resolve(undefined),
}));

function renderNavbar() {
  return render(
    <RefreshProvider>
      <Navbar />
    </RefreshProvider>
  );
}

describe("Navbar", () => {
  it("renders nothing when no user is authenticated", () => {
    mockUseAuth.mockReturnValue({ user: null, logoutUser: vi.fn() });
    const { container } = renderNavbar();

    expect(container).toBeEmptyDOMElement();
  });

  it("renders the navbar, including the user avatar, once authenticated", async () => {
    mockUseAuth.mockReturnValue({
      user: {
        id: "mock-user-1",
        email: "demo.hq@futurekawa.com",
        role: "OperationsSupplyChain",
        country: "HQ",
        warehouseId: null,
      },
      logoutUser: vi.fn(),
    });
    renderNavbar();

    expect(await screen.findByText("Opérations & Supply Chain")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rafraîchir les données" })).toBeInTheDocument();
  });
});