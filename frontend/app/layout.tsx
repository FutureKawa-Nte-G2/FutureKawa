import type { Metadata } from "next";
import { Geist_Mono, Poppins, REM } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "../context/AuthContext";

// Body copy: titles, subtitles, labels, buttons
const poppins = Poppins({
  variable: "--font-poppins",
  subsets: ["latin"],
  weight: ["400", "500", "700"],
});

// User-typed input text only (email/password fields)
const rem = REM({
  variable: "--font-rem",
  subsets: ["latin"],
  weight: ["500"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "FutureKawa FIFO",
  description: "Follow the status of the batches.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="fr"
      className={`${poppins.variable} ${rem.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <AuthProvider>{children}</AuthProvider>
      </body>
    </html>
  );
}