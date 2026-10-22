"use client";

import { useEffect, useRef, useState } from "react";

export type RetailCommand = { operation: string; targetId: string; body: Record<string, unknown> };

export function useRetailCommand(key: string) {
  const [pending, setPending] = useState<RetailCommand | null>(null);
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const sending = useRef(false);
  useEffect(() => {
    setReady(false);
    try {
      const saved = sessionStorage.getItem(key);
      setPending(saved ? JSON.parse(saved) : null); setReady(true); setError("");
    } catch { setError("Saved command cannot be read. Operator review is required."); }
  }, [key]);
  async function run<T>(command: RetailCommand, send: (command: RetailCommand) => Promise<T>): Promise<T | undefined> {
    if (!ready || sending.current) return;
    sending.current = true; setBusy(true); setError("");
    const frozen = pending ?? command;
    try {
      sessionStorage.setItem(key, JSON.stringify(frozen)); setPending(frozen);
      const result = await send(frozen);
      sessionStorage.removeItem(key); setPending(null);
      return result;
    } catch (e) {
      const status = (e as { status?: number }).status;
      if (status && status >= 400 && status < 500 && ![401, 403, 408, 429].includes(status)) {
        sessionStorage.removeItem(key); setPending(null);
      }
      setError(e instanceof Error ? e.message : "Command failed. Retry the original request.");
    } finally { sending.current = false; setBusy(false); }
  }
  return { pending, busy, ready, error, run };
}
