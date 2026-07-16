import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LoginForm } from "./LoginForm";
import { ApiError } from "../../lib/api/client";

// Mock the auth context so the form never hits the real network layer.
const mockLoginUser = vi.fn();
vi.mock("../../context/AuthContext", () => ({
  useAuth: () => ({ loginUser: mockLoginUser }),
}));

// Mock Next.js navigation, unused by this router mock but required by the component import.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("LoginForm", () => {
  beforeEach(() => {
    mockLoginUser.mockReset();
  });

  it("shows a required-field error when submitting an empty form", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.click(screen.getByRole("button", { name: /se connecter/i }));

    expect(screen.getByText("L'email est requis.")).toBeInTheDocument();
    expect(screen.getByText("Le mot de passe est requis.")).toBeInTheDocument();
    expect(mockLoginUser).not.toHaveBeenCalled();
  });

  it("shows a format error for an invalid email", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "not-an-email");
    await user.type(screen.getByLabelText("Mot de passe"), "somepassword");
    await user.click(screen.getByRole("button", { name: /se connecter/i }));

    expect(screen.getByText("Format d'email invalide.")).toBeInTheDocument();
    expect(mockLoginUser).not.toHaveBeenCalled();
  });

  it("shows the generic error message when login fails", async () => {
    mockLoginUser.mockRejectedValueOnce(new Error("network down"));
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "test@futurekawa.com");
    await user.type(screen.getByLabelText("Mot de passe"), "TestPass123");
    await user.click(screen.getByRole("button", { name: /se connecter/i }));

    expect(
      await screen.findByText("Une erreur est survenue. Veuillez réessayer.")
    ).toBeInTheDocument();
  });

  it("toggles password visibility when clicking the eye icon", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    const passwordInput = screen.getByLabelText("Mot de passe");
    expect(passwordInput).toHaveAttribute("type", "password");

    await user.click(screen.getByLabelText("Afficher le mot de passe"));
    expect(passwordInput).toHaveAttribute("type", "text");
  });

  it("shows the generic error message on an ApiError (e.g. invalid credentials)", async () => {
    mockLoginUser.mockRejectedValueOnce(new ApiError("Invalid credentials", 401));
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("Email"), "test@futurekawa.com");
    await user.type(screen.getByLabelText("Mot de passe"), "wrongpassword");
    await user.click(screen.getByRole("button", { name: /se connecter/i }));

    expect(
      await screen.findByText("Email ou mot de passe incorrect.")
    ).toBeInTheDocument();
    // Confirms the real backend message ("Invalid credentials") is never shown to the user
    expect(screen.queryByText("Invalid credentials")).not.toBeInTheDocument();
  });
});