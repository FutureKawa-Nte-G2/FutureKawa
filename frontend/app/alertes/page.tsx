import type { Metadata } from "next";
import { AuthGate } from "@/components/auth/AuthGate";

export const metadata: Metadata = {
  title: "FutureKawa — Alertes",
};

// Placeholder route so the sidebar's "Alertes" link (#74) is live rather than
// dangling. Real content (historique des alertes) is developed in a separate
// issue, blocked on GET /api/alerts existing server-side — see
// backlog-frontend-mesures-alertes-collecte.md.
export default function AlertesPage() {
  return (
    <AuthGate>
      <main className="flex-1 p-8">
        <h1 className="text-2xl font-semibold text-foreground">Alertes</h1>
        <p className="mt-1 text-sm text-input-text">
          Cette page est en cours de construction — l&apos;historique des alertes sera disponible
          ici prochainement.
        </p>
      </main>
    </AuthGate>
  );
}
