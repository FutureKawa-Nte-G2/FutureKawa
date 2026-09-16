import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { Sidebar } from "./Sidebar";

const mockUseAuth = vi.fn();
const mockUsePathname = vi.fn();

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock("next/navigation", () => ({
  usePathname: () => mockUsePathname(),
}));

const authenticatedUser = {
  id: "mock-user-1",
  email: "demo.hq@futurekawa.com",
  role: "WarehouseManager",
  warehouseId: null,
};

describe("Sidebar", () => {
  it("renders nothing when no user is authenticated", () => {
    mockUseAuth.mockReturnValue({ user: null });
    mockUsePathname.mockReturnValue("/");
    const { container } = render(<Sidebar />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders links to FIFO and Alertes once authenticated", () => {
    mockUseAuth.mockReturnValue({ user: authenticatedUser });
    mockUsePathname.mockReturnValue("/fifo");
    render(<Sidebar />);

    expect(screen.getByRole("link", { name: "FIFO" })).toHaveAttribute("href", "/fifo");
    expect(screen.getByRole("link", { name: "Alertes" })).toHaveAttribute("href", "/alertes");
  });

  it("marks the current page's link as active", () => {
    mockUseAuth.mockReturnValue({ user: authenticatedUser });
    mockUsePathname.mockReturnValue("/fifo");
    render(<Sidebar />);

    expect(screen.getByRole("link", { name: "FIFO" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Alertes" })).not.toHaveAttribute("aria-current");
  });
});
