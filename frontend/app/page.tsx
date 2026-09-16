import type { Metadata } from "next";
import { LoginGate } from "../components/auth/LoginGate";

export const metadata: Metadata = {
  title: "Connexion — FutureKawaHub",
};

export default function Home() {
  return <LoginGate />;
}