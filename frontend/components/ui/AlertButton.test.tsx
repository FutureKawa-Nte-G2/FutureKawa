import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertButton } from "./AlertButton";
import type { Alert } from "@/lib/api/types";

const mockGetUnreadAlerts = vi.fn();
const mockPush = vi.fn();
let alertsChangeListener: (() => void) | null = null;

vi.mock("@/lib/api/alerts", () => ({
  getUnreadAlerts: () => mockGetUnreadAlerts(),
  subscribeToAlertsChange: (listener: () => void) => {
    alertsChangeListener = listener;
    return () => {
      alertsChangeListener = null;
    };
  },
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-access-token" }),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

const alerts: Alert[] = [
  {
    id: "1",
    warehouseId: "wh-1",
    warehouseName: "Cerrado",
    countryCode: "BR",
    countryName: "Brésil",
    type: "temperature",
    status: "active",
    createdAt: "2026-07-27T08:15:00Z",
    resolvedAt: null,
    measuredAt: "2026-07-27T08:00:00Z",
  },
  {
    id: "2",
    warehouseId: "wh-2",
    warehouseName: "Santos",
    countryCode: "BR",
    countryName: "Brésil",
    type: "humidity",
    status: "active",
    createdAt: "2026-06-01T14:15:00Z",
    resolvedAt: null,
    measuredAt: "2026-06-01T14:00:00Z",
  },
];

describe("AlertButton", () => {
  beforeEach(() => {
    mockGetUnreadAlerts.mockReset().mockResolvedValue(alerts);
    mockPush.mockReset();
    alertsChangeListener = null;
  });

  it("displays the unread count from the API", async () => {
    render(<AlertButton />);
    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("navigates to /alertes and closes the menu when an alert is clicked, without resolving it", async () => {
    const user = userEvent.setup();
    render(<AlertButton />);

    await user.click(await screen.findByRole("button", { name: /Alertes non lues \(2\)/ }));
    await user.click(await screen.findByRole("menuitem", { name: /Entrepôt Cerrado/ }));

    expect(mockPush).toHaveBeenCalledWith("/alertes");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("shows the empty state and hides the badge once there are no active alerts", async () => {
    mockGetUnreadAlerts.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<AlertButton />);

    await user.click(await screen.findByRole("button", { name: /Alertes non lues \(0\)/ }));

    expect(screen.getByText("Aucune alerte non lue")).toBeInTheDocument();
    expect(screen.queryByText("0")).not.toBeInTheDocument();
  });

  it("refetches the unread count when an alert is acknowledged elsewhere", async () => {
    render(<AlertButton />);
    expect(await screen.findByText("2")).toBeInTheDocument();

    mockGetUnreadAlerts.mockResolvedValue([alerts[0]]);
    alertsChangeListener?.();

    expect(await screen.findByText("1")).toBeInTheDocument();
  });
});
