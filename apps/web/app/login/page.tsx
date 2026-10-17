"use client";

import { FormEvent, useState } from "react";
import { LogIn } from "lucide-react";
import { login } from "../../lib/api";

export default function LoginPage() {
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setBusy(true); setError("");
    try { await login(String(data.get("email")), String(data.get("password"))); window.location.assign("/pos"); }
    catch (e) { setError(e instanceof Error ? e.message : "Sign in failed"); setBusy(false); }
  }
  return <main className="login-page"><form className="form-panel" onSubmit={submit}><h1>Pilot POS</h1><p>One POS for every business.</p>
    <label>Email<input name="email" type="email" autoComplete="username" required autoFocus/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required/></label>
    {error && <p role="alert" className="sync-error">{error}</p>}
    <button className="primary-link" disabled={busy}><LogIn size={18}/>{busy ? "Signing in..." : "Sign in"}</button>
  </form></main>;
}
