import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RefreshButton } from "./RefreshButton";

const mockRefresh = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: mockRefresh }),
}));

describe("RefreshButton", () => {
  beforeEach(() => {
    mockRefresh.mockReset();
  });

  it("calls router.refresh() when clicked", async () => {
    const user = userEvent.setup();
    render(<RefreshButton />);

    await user.click(screen.getByRole("button", { name: "Rafraîchir les données" }));

    expect(mockRefresh).toHaveBeenCalledTimes(1);
  });
});