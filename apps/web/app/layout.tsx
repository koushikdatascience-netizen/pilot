import type { Metadata } from "next";
import "./styles.css";

export const metadata: Metadata = {
  title: "Universal Commerce OS",
  description: "Retail POS foundation for Universal Commerce OS"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
