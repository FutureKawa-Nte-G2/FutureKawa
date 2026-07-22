import { describe, it, expect, beforeAll } from "vitest";
import type { Batch, Country, Warehouse } from "./types";

let getBatches: (params?: { countryCode?: string; warehouseId?: string }) => Promise<Batch[]>;
let getCountries: () => Promise<Country[]>;
let getWarehouses: (countryCode?: string) => Promise<Warehouse[]>;

beforeAll(async () => {
  process.env.NEXT_PUBLIC_USE_MOCKS = "true";
  ({ getBatches, getCountries, getWarehouses } = await import("./batches"));
});

describe("getBatches", () => {
  it("returns batches sorted oldest-first", async () => {
    const batches = await getBatches();
    expect(batches.map((b) => b.id)).toEqual(["1", "2", "3", "4", "5", "6"]);
  });

  it("filters by countryCode", async () => {
    const brazil = await getBatches({ countryCode: "BR" });
    const ecuador = await getBatches({ countryCode: "EC" });

    expect(brazil).toHaveLength(6);
    expect(ecuador).toHaveLength(0);
  });

  it("filters by warehouseId", async () => {
    const santos = await getBatches({ warehouseId: "BR-W1" });
    const cerrado = await getBatches({ warehouseId: "BR-W2" });

    expect(santos.map((b) => b.id)).toEqual(["1", "3", "5"]);
    expect(cerrado.map((b) => b.id)).toEqual(["2", "4", "6"]);
  });

  it("combines countryCode and warehouseId filters", async () => {
    const batches = await getBatches({ countryCode: "BR", warehouseId: "BR-W1" });
    expect(batches.map((b) => b.id)).toEqual(["1", "3", "5"]);
  });

  it("returns every batch when no filter is given", async () => {
    const batches = await getBatches({});
    expect(batches).toHaveLength(6);
  });
});

describe("getCountries", () => {
  it("returns the three configured countries", async () => {
    const countries = await getCountries();
    expect(countries.map((c) => c.code)).toEqual(["BR", "EC", "CO"]);
  });
});

describe("getWarehouses", () => {
  it("filters warehouses by country", async () => {
    const brWarehouses = await getWarehouses("BR");
    const ecWarehouses = await getWarehouses("EC");

    expect(brWarehouses.map((w) => w.id)).toEqual(["BR-W1", "BR-W2"]);
    expect(ecWarehouses.map((w) => w.id)).toEqual(["EC-W1"]);
  });

  it("returns every warehouse when no country is given", async () => {
    const warehouses = await getWarehouses();
    expect(warehouses).toHaveLength(4);
  });
});