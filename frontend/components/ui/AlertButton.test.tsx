import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AlertButton } from "./AlertButton";

const mockGetUnreadAlerts = vi.fn();
const mockMarkAlertAsRead = vi.fn();

vi.mock("@/lib/api/alerts", () => ({
  getUnreadAlerts: () => mockGetUnreadAlerts(),
  markAlertAsRead: (id: string) => mockMarkAlertAsRead(id),
}));

const alerts = [
  { id: "alert-1", kind: "expired" as const, message: "Lot BR-2026-0341 périmé.", createdAt: "2026-07-27T08:15:00Z" },
  { id: "alert-2", kind: "alert" as const, message: "Conditions hors plage à Cuenca-2.", createdAt: "2026-07-26T14:32:00Z" },
];

describe("AlertButton", () => {
  beforeEach(() => {
    mockGetUnreadAlerts.mockReset().mockResolvedValue(alerts);
    mockMarkAlertAsRead.mockReset().mockResolvedValue(undefined);
  });

  it("displays the unread count from the API", async () => {
    render(<AlertButton />);
    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("removes an alert from the list and decrements the badge when clicked", async () => {
    const user = userEvent.setup();
    render(<AlertButton />);

    await user.click(await screen.findByRole("button", { name: /Alertes non lues \(2\)/ }));
    await user.click(await screen.findByRole("menuitem", { name: /Lot BR-2026-0341 périmé/ }));

    expect(mockMarkAlertAsRead).toHaveBeenCalledWith("alert-1");
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