import type { Metadata } from "next";
import "./styles.css";

export const metadata: Metadata = {
  title: "Pilot POS",
  description: "Retail POS core for Pilot POS"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
