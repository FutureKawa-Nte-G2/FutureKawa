import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RefreshButton } from "./RefreshButton";

const mockTriggerRefresh = vi.fn();
const mockSyncMeasurements = vi.fn();

vi.mock("@/context/RefreshContext", () => ({
  useRefresh: () => ({ triggerRefresh: mockTriggerRefresh }),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-token" }),
}));

vi.mock("@/lib/api/measurements", () => ({
  syncMeasurements: (...args: unknown[]) => mockSyncMeasurements(...args),
}));

describe("RefreshButton", () => {
  beforeEach(() => {
    mockTriggerRefresh.mockReset();
    mockSyncMeasurements.mockReset();
    mockSyncMeasurements.mockResolvedValue("Sync completed.");
  });

  it("triggers a measurement sync with the access token, then calls triggerRefresh(), when clicked", async () => {
    const user = userEvent.setup();
    render(<RefreshButton />);

    await user.click(screen.getByRole("button", { name: "Rafraîchir les données" }));

    expect(mockSyncMeasurements).toHaveBeenCalledWith("test-token");
    expect(mockTriggerRefresh).toHaveBeenCalledTimes(1);
  });

  it("still calls triggerRefresh() when the sync call fails", async () => {
    mockSyncMeasurements.mockRejectedValueOnce(new Error("network error"));
    const user = userEvent.setup();
    render(<RefreshButton />);

    await user.click(screen.getByRole("button", { name: "Rafraîchir les données" }));

    expect(mockTriggerRefresh).toHaveBeenCalledTimes(1);
  });

  it("shows an error message when the sync call fails, without throwing", async () => {
    mockSyncMeasurements.mockRejectedValueOnce(new Error("network error"));
    const user = userEvent.setup();
    render(<RefreshButton />);

    await user.click(screen.getByRole("button", { name: "Rafraîchir les données" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/collecte des mesures a échoué/i);
  });

  it("clears a previous error on the next successful click", async () => {
    mockSyncMeasurements.mockRejectedValueOnce(new Error("network error"));
    const user = userEvent.setup();
    render(<RefreshButton />);
    const button = screen.getByRole("button", { name: "Rafraîchir les données" });

    await user.click(button);
    expect(await screen.findByRole("alert")).toBeInTheDocument();

    await user.click(button);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});