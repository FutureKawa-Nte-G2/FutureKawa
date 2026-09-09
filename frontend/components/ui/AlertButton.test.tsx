import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertButton } from "./AlertButton";

const mockGetUnreadAlerts = vi.fn();
const mockResolveAlert = vi.fn();

vi.mock("@/lib/api/alerts", () => ({
  getUnreadAlerts: () => mockGetUnreadAlerts(),
  resolveAlert: (id: string) => mockResolveAlert(id),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-access-token" }),
}));

const alerts = [
  {
    id: "1",
    alertType: "expired" as const,
    alertStatus: "open" as const,
    batchRef: "BR-2026-0341",
    warehouseName: "Cerrado",
    message: "Le lot BR-2026-0341 a dépassé 365 jours de stockage dans l'entrepôt Cerrado (Brésil).",
    createdAt: "2026-07-27T08:15:00Z",
    resolvedAt: null,
  },
  {
    id: "2",
    alertType: "alert" as const,
    alertStatus: "open" as const,
    batchRef: null,
    warehouseName: "Santos",
    message: "Conditions hors plage détectée dans l'entrepôt de Santos (Brésil).",
    createdAt: "2026-06-01T14:15:00Z",
    resolvedAt: null,
  },
];

describe("AlertButton", () => {
  beforeEach(() => {
    mockGetUnreadAlerts.mockReset().mockResolvedValue(alerts);
    mockResolveAlert.mockReset().mockResolvedValue(undefined);
  });

  it("displays the unread count from the API", async () => {
    render(<AlertButton />);
    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("removes an alert from the list and decrements the badge when clicked", async () => {
    const user = userEvent.setup();
    render(<AlertButton />);

    await user.click(await screen.findByRole("button", { name: /Alertes non lues \(2\)/ }));
    await user.click(await screen.findByRole("menuitem", { name: /BR-2026-0341/ }));

    expect(mockResolveAlert).toHaveBeenCalledWith("1");
    expect(screen.queryByText(/Lot BR-2026-0341/)).not.toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  it("shows the empty state and hides the badge once all alerts are read", async () => {
    mockGetUnreadAlerts.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<AlertButton />);

    await user.click(await screen.findByRole("button", { name: /Alertes non lues \(0\)/ }));

    expect(screen.getByText("Aucune alerte non lue")).toBeInTheDocument();
    expect(screen.queryByText("0")).not.toBeInTheDocument();
  });
});