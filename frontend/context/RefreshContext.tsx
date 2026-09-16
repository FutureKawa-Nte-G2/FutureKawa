"use client";

import { createContext, useCallback, useContext, useRef, type ReactNode } from "react";

type RefreshHandler = () => Promise<void> | void;

interface RefreshContextValue {
  // Called by the currently mounted page to declare what "refresh" means for it.
  // Passing null clears the handler (e.g. on unmount), so a stale page doesn't
  // keep responding to refresh clicks after navigation.
  registerRefreshHandler: (handler: RefreshHandler | null) => void;
  // Called by RefreshButton; delegates to whatever the current page registered.
  triggerRefresh: () => Promise<void>;
}

const RefreshContext = createContext<RefreshContextValue | undefined>(undefined);

export function RefreshProvider({ children }: { children: ReactNode }) {
  const handlerRef = useRef<RefreshHandler | null>(null);

  const registerRefreshHandler = useCallback((handler: RefreshHandler | null) => {
    handlerRef.current = handler;
  }, []);

  const triggerRefresh = useCallback(async () => {
    await handlerRef.current?.();
  }, []);

  return (
    <RefreshContext.Provider value={{ registerRefreshHandler, triggerRefresh }}>
      {children}
    </RefreshContext.Provider>
  );
}

export function useRefresh(): RefreshContextValue {
  const context = useContext(RefreshContext);
  if (!context) {
    throw new Error("useRefresh must be used within a RefreshProvider");
  }
  return context;
}