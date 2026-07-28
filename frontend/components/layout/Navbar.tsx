"use client";

import Link from "next/link";
import { useAuth } from "@/context/AuthContext";
import { UserMenu } from "@/components/layout/UserMenu";
import { RefreshButton } from "../ui/RefreshButton";
import { AlertButton } from "../ui/AlertButton";

export function Navbar() {
  const { user } = useAuth();

  if (!user) {
    return null;
  }

  return (
    <header className="sticky top-0 z-40 flex h-(--navbar-height) w-full border-b border-border-primary bg-background">
      {/* Logo band — same width AND background as the sidebar, so both stay visually continuous */}
      <div className="flex w-(--sidebar-width) shrink-0 items-center justify-center bg-background-secondary px-6">
        <Link href="/dashboard" className="text-logo font-bold text-brand">
          Futurekawa
        </Link>
      </div>

      {/* Main navbar content: search (left) + actions (right) */}
      <div className="flex flex-1 items-center justify-between gap-4 px-6">
        <div className="max-w-md flex-1">
          {/* SearchInput — placeholder only, wired in a following increment */}
        </div>

        <div className="flex items-center gap-3">
          <AlertButton />
          <RefreshButton />
          <UserMenu />
        </div>
      </div>
    </header>
  );
}