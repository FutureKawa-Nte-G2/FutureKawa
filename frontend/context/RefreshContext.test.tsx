import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useEffect } from "react";
import { RefreshProvider, useRefresh } from "./RefreshContext";

function Consumer({ handler }: { handler: () => void }) {
  const { registerRefreshHandler, triggerRefresh } = useRefresh();

  useEffect(() => {
    registerRefreshHandler(handler);
    return () => registerRefreshHandler(null);
  }, [registerRefreshHandler, handler]);

  return (
    <button type="button" onClick={() => triggerRefresh()}>
      Trigger
    </button>
  );
}

// A minimal consumer that only calls triggerRefresh, without ever registering a handler.
function TriggerOnlyButton() {
  const { triggerRefresh } = useRefresh();
  return (
    <button type="button" onClick={() => triggerRefresh()}>
      Trigger without registering
    </button>
  );
}

describe("RefreshContext", () => {
  it("calls the registered handler when triggerRefresh is invoked", async () => {
    const handler = vi.fn();
    const user = userEvent.setup();
    render(
      <RefreshProvider>
        <Consumer handler={handler} />
      </RefreshProvider>
    );

    await user.click(screen.getByRole("button", { name: "Trigger" }));

    expect(handler).toHaveBeenCalledTimes(1);
  });

  it("does nothing when no handler is registered", async () => {
    const user = userEvent.setup();
    render(
      <RefreshProvider>
        <TriggerOnlyButton />
      </RefreshProvider>
    );

    await expect(
      user.click(screen.getByRole("button", { name: "Trigger without registering" }))
    ).resolves.not.toThrow();
  });

  it("stops calling a handler after it unregisters (e.g. on unmount)", async () => {
    const handler = vi.fn();
    const user = userEvent.setup();

    function ExternalTrigger() {
      const { triggerRefresh } = useRefresh();
      return (
        <button type="button" onClick={() => triggerRefresh()}>
          External trigger
        </button>
      );
    }

    function Wrapper({ mounted }: { mounted: boolean }) {
      return (
        <RefreshProvider>
          {mounted && <Consumer handler={handler} />}
          <ExternalTrigger />
        </RefreshProvider>
      );
    }

    const { rerender } = render(<Wrapper mounted={true} />);
    await user.click(screen.getByRole("button", { name: "External trigger" }));
    expect(handler).toHaveBeenCalledTimes(1);

    rerender(<Wrapper mounted={false} />);
    await user.click(screen.getByRole("button", { name: "External trigger" }));
    expect(handler).toHaveBeenCalledTimes(1); // still 1, not 2 — unregistered on unmount
  });
});