"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "../../context/AuthContext";
import { LoginForm } from "./LoginForm";

// Renders the login form, or redirects to /dashboard if a silent
// reconnection (via the refresh cookie) already succeeded on mount.
export function LoginGate() {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!isLoading && isAuthenticated) {
      router.replace("/dashboard");
    }
  }, [isLoading, isAuthenticated, router]);

  // While checking, or right before the redirect above kicks in,
  // avoid flashing the login form.
  if (isLoading || isAuthenticated) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-gray-400">Chargement...</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen">
      <div className="flex w-full flex-col justify-center px-8 md:w-1/2 md:px-16">
        <LoginForm />
      </div>
      {/* TODO: replace with the real Figma export, e.g. public/images/login-hero.jpg */}
      <div className="hidden md:block md:w-1/2 bg-gray-200" />
    </div>
  );
}