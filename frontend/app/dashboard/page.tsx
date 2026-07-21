"use client";

import { useState } from "react";
import { LocationFilter, type LocationSelection } from "@/components/batches/LocationFilter";

export default function DashboardPage() {
  const [selection, setSelection] = useState<LocationSelection>({
    country: null,
    warehouse: null,
  });

  const title = selection.warehouse
    ? `${selection.country?.name} — ${selection.warehouse.name}`
    : selection.country?.name ?? "Tous les pays";

  return (
    <div className="flex min-h-screen">
      <LocationFilter onSelectionChange={setSelection} />
      <main className="flex-1 p-8">
        <h1 className="text-2xl font-semibold text-foreground">{title}</h1>
        <p className="mt-1 text-sm text-input-text">
          Suivi des stocks et accès aux courbes de mesures qualité.
        </p>
        <div className="mt-8 text-sm text-input-text">
          Tableau des lots en stock.
        </div>
      </main>
    </div>
  );
}