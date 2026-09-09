"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/context/AuthContext";

// Wraps a protected page's content. Mirrors LoginGate (which redirects an
// already-authenticated user away from "/"), but in reverse: waits for the
// silent-reconnection check (AuthContext) to finish, redirects to "/" if it
// turns out the user isn't authenticated, and only renders `children` once
// a valid session is confirmed.
export function AuthGate({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!isLoading && !isAuthenticated) {
      router.replace("/");
    }
  }, [isLoading, isAuthenticated, router]);

  // While checking, or right before the redirect above kicks in, avoid
  // rendering (and thus mounting) protected content.
  if (isLoading || !isAuthenticated) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-gray-400">Chargement...</p>
      </div>
    );
  }

  return <>{children}</>;
}
