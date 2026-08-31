import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RefreshButton } from "./RefreshButton";

const mockTriggerRefresh = vi.fn();

vi.mock("@/context/RefreshContext", () => ({
  useRefresh: () => ({ triggerRefresh: mockTriggerRefresh }),
}));

describe("RefreshButton", () => {
  beforeEach(() => {
    mockTriggerRefresh.mockReset();
  });

  it("calls triggerRefresh() when clicked", async () => {
    const user = userEvent.setup();
    render(<RefreshButton />);

    await user.click(screen.getByRole("button", { name: "Rafraîchir les données" }));

    expect(mockTriggerRefresh).toHaveBeenCalledTimes(1);
  });
});