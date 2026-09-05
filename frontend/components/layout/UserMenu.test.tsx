import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UserMenu } from "./UserMenu";

const mockLogoutUser = vi.fn();
const mockPush = vi.fn();

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({
    user: {
      id: "mock-user-1",
      email: "demo.hq@futurekawa.com",
      role: "OperationsSupplyChain",
      country: "HQ",
      warehouseId: null,
    },
    logoutUser: mockLogoutUser,
  }),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

describe("UserMenu", () => {
  beforeEach(() => {
    mockLogoutUser.mockReset().mockResolvedValue(undefined);
    mockPush.mockReset();
  });

  it("calls logoutUser and redirects to the login page when 'Se déconnecter' is clicked", async () => {
    const user = userEvent.setup();
    render(<UserMenu />);

    await user.click(screen.getByRole("button", { name: /Opérations & Supply Chain/ }));
    await user.click(screen.getByRole("menuitem", { name: "Se déconnecter" }));

    expect(mockLogoutUser).toHaveBeenCalledTimes(1);
    expect(mockPush).toHaveBeenCalledWith("/");
  });

  it("disables the logout item and shows a loading label while logging out", async () => {
    let resolveLogout!: () => void;
    mockLogoutUser.mockReturnValue(
      new Promise<void>((resolve) => {
        resolveLogout = resolve;
      })
    );

    const user = userEvent.setup();
    render(<UserMenu />);

    await user.click(screen.getByRole("button", { name: /Opérations & Supply Chain/ }));
    await user.click(screen.getByRole("menuitem", { name: "Se déconnecter" }));

    expect(screen.getByRole("menuitem", { name: "Déconnexion..." })).toBeDisabled();

    resolveLogout();
  });
});