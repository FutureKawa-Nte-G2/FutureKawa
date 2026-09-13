"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/context/AuthContext";

const NAV_LINKS = [
  { href: "/fifo", label: "FIFO" },
  { href: "/alertes", label: "Alertes" },
];

export function Sidebar() {
  const { user } = useAuth();
  const pathname = usePathname();

  if (!user) {
    return null;
  }

  return (
    <nav
      aria-label="Navigation principale"
      className="flex w-(--sidebar-width) shrink-0 flex-col gap-1 bg-background-secondary p-4"
    >
      {NAV_LINKS.map((link) => {
        const isActive = pathname === link.href || pathname?.startsWith(`${link.href}/`);
        return (
          <Link
            key={link.href}
            href={link.href}
            aria-current={isActive ? "page" : undefined}
            className={`rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
              isActive
                ? "bg-brand text-white"
                : "text-input-text hover:bg-background hover:text-foreground"
            }`}
          >
            {link.label}
          </Link>
        );
      })}
    </nav>
  );
}
