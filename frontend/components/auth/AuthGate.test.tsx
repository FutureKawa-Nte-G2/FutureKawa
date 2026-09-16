import { useEffect } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuthGate } from "./AuthGate";

const mockReplace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: mockReplace }),
}));

const mockUseAuth = vi.fn();
vi.mock("@/context/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

// Tracks whether AuthGate's children were actually mounted 
const onChildMount = vi.fn();
function ChildProbe() {
  useEffect(() => {
    onChildMount();
  }, []);
  return <div>protected content</div>;
}

describe("AuthGate", () => {
  beforeEach(() => {
    mockReplace.mockReset();
    onChildMount.mockReset();
  });

  it("shows a loading state and does not mount children while isLoading is true", () => {
    mockUseAuth.mockReturnValue({ isAuthenticated: false, isLoading: true });

    render(
      <AuthGate>
        <ChildProbe />
      </AuthGate>
    );

    expect(screen.getByText("Chargement...")).toBeInTheDocument();
    expect(screen.queryByText("protected content")).not.toBeInTheDocument();
    expect(onChildMount).not.toHaveBeenCalled();
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('redirects to "/" and does not mount children once loading finishes unauthenticated', () => {
    mockUseAuth.mockReturnValue({ isAuthenticated: false, isLoading: false });

    render(
      <AuthGate>
        <ChildProbe />
      </AuthGate>
    );

    expect(mockReplace).toHaveBeenCalledWith("/");
    expect(screen.queryByText("protected content")).not.toBeInTheDocument();
    expect(onChildMount).not.toHaveBeenCalled();
  });

  it("mounts children and does not redirect once authenticated", () => {
    mockUseAuth.mockReturnValue({ isAuthenticated: true, isLoading: false });

    render(
      <AuthGate>
        <ChildProbe />
      </AuthGate>
    );

    expect(screen.getByText("protected content")).toBeInTheDocument();
    expect(onChildMount).toHaveBeenCalledTimes(1);
    expect(mockReplace).not.toHaveBeenCalled();
  });
});