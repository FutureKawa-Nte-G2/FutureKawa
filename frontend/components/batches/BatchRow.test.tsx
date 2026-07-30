import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BatchRow } from "./BatchRow";
import type { Batch } from "@/lib/api/types";

const mockPush = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

function makeBatch(overrides: Partial<Batch>): Batch {
  return {
    id: "42",
    countryCode: "BR",
    countryName: "Brésil",
    warehouseId: "BR-W1",
    warehouseName: "Test Warehouse",
    farmName: "Fazenda Test",
    batchRef: "ERP-BR-2026-0042",
    qualityGrade: "Premium",
    status: "compliant",
    enteredAt: "2026-01-01T00:00:00Z",
    shippedAt: null,
    ...overrides,
  };
}

describe("BatchRow", () => {
  beforeEach(() => {
    mockPush.mockReset();
  });

  it("displays the ERP batch reference, not the internal id", () => {
    const batch = makeBatch({ id: "42", batchRef: "ERP-BR-2026-0042" });
    render(<BatchRow batch={batch} />);

    expect(screen.getByText("ERP-BR-2026-0042")).toBeInTheDocument();
    expect(screen.queryByText("42")).not.toBeInTheDocument();
  });

  it("displays farm, warehouse and a formatted entry date", () => {
    const batch = makeBatch({
      farmName: "Fazenda Boa Vista",
      warehouseName: "Santos Warehouse",
      enteredAt: "2026-07-01T07:45:00Z",
    });
    render(<BatchRow batch={batch} />);

    expect(screen.getByText("Fazenda Boa Vista")).toBeInTheDocument();
    expect(screen.getByText("Santos Warehouse")).toBeInTheDocument();
    expect(screen.getByText("1 juil. 2026")).toBeInTheDocument();
  });

  it("displays the status badge", () => {
    render(<BatchRow batch={makeBatch({ status: "alert" })} />);
    expect(screen.getByText("alerte")).toBeInTheDocument();
  });

  it("navigates to the batch detail route on click", async () => {
    const user = userEvent.setup();
    const batch = makeBatch({ id: "42", countryCode: "BR" });
    render(<BatchRow batch={batch} />);

    await user.click(screen.getByRole("button", { name: "Suivi qualité" }));

    expect(mockPush).toHaveBeenCalledWith("/batches/BR/42");
  });
});