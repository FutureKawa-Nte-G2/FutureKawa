import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LocationFilter } from "./LocationFilter";
import type { Country, Warehouse } from "@/lib/api/types";

const mockGetCountries = vi.fn();
const mockGetWarehouses = vi.fn();

vi.mock("@/lib/api/batches", () => ({
  getCountries: (accessToken?: string | null) => mockGetCountries(accessToken),
  getWarehouses: (countryCode?: string, accessToken?: string | null) =>
    mockGetWarehouses(countryCode, accessToken),
}));

vi.mock("@/context/AuthContext", () => ({
  useAuth: () => ({ accessToken: "test-access-token" }),
}));

const BRAZIL: Country = { code: "BR", name: "Brésil" };
const SANTOS: Warehouse = { id: "wh-santos", countryCode: "BR", name: "Santos" };
const CERRADO: Warehouse = { id: "wh-cerrado", countryCode: "BR", name: "Cerrado" };

describe("LocationFilter", () => {
  beforeEach(() => {
    mockGetCountries.mockReset();
    mockGetWarehouses.mockReset();
    mockGetCountries.mockResolvedValue([BRAZIL]);
    mockGetWarehouses.mockResolvedValue([SANTOS, CERRADO]);
  });

  it("does not notify the parent when there is nothing to restore", async () => {
    const onSelectionChange = vi.fn();
    render(<LocationFilter onSelectionChange={onSelectionChange} />);

    await waitFor(() => expect(mockGetCountries).toHaveBeenCalled());
    expect(onSelectionChange).not.toHaveBeenCalled();
  });

  // Covers the FIFO page carrying a previously-selected country/warehouse in
  // via the URL (see app/fifo/page.tsx): LocationFilter only knows the bare
  // codes up front and must resolve them into full Country/Warehouse objects
  // once the reference data has loaded, notifying the parent exactly once.
  it("restores a country and warehouse from initial props once reference data loads", async () => {
    const onSelectionChange = vi.fn();
    render(
      <LocationFilter
        onSelectionChange={onSelectionChange}
        initialCountryCode="BR"
        initialWarehouseId="wh-cerrado"
      />
    );

    await waitFor(() =>
      expect(onSelectionChange).toHaveBeenCalledWith({
        country: BRAZIL,
        warehouse: CERRADO,
        qualityGrade: null,
      })
    );
    expect(mockGetWarehouses).toHaveBeenCalledWith("BR", "test-access-token");

    // Never fires again once resolved — a repeat call would re-trigger the
    // FIFO page's batch fetch on every unrelated re-render.
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(onSelectionChange).toHaveBeenCalledTimes(1);
  });

  it("restores a country-only selection without waiting on a warehouse", async () => {
    const onSelectionChange = vi.fn();
    render(<LocationFilter onSelectionChange={onSelectionChange} initialCountryCode="BR" />);

    await waitFor(() =>
      expect(onSelectionChange).toHaveBeenCalledWith({
        country: BRAZIL,
        warehouse: null,
        qualityGrade: null,
      })
    );
    expect(onSelectionChange).toHaveBeenCalledTimes(1);
  });

  it("restores the country and clears a stale/unknown warehouse id", async () => {
    const onSelectionChange = vi.fn();
    render(
      <LocationFilter
        onSelectionChange={onSelectionChange}
        initialCountryCode="BR"
        initialWarehouseId="wh-does-not-exist"
      />
    );

    await waitFor(() =>
      expect(onSelectionChange).toHaveBeenCalledWith({
        country: BRAZIL,
        warehouse: null,
        qualityGrade: null,
      })
    );
    expect(onSelectionChange).toHaveBeenCalledTimes(1);

    const warehouseSelect = screen.getByLabelText("Entrepôt") as HTMLSelectElement;
    expect(warehouseSelect.value).toBe("ALL_WAREHOUSES");
  });

  it("still reports user-driven selection changes normally", async () => {
    const user = userEvent.setup();
    const onSelectionChange = vi.fn();
    render(<LocationFilter onSelectionChange={onSelectionChange} />);

    await waitFor(() => expect(screen.getByLabelText("Pays")).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText("Pays"), "BR");

    await waitFor(() =>
      expect(onSelectionChange).toHaveBeenCalledWith({
        country: BRAZIL,
        warehouse: null,
        qualityGrade: null,
      })
    );
  });
});
