"use client";

import { ArrowLeft, ChevronLeft, ChevronRight, FileSearch, KeyRound, Pencil, Plus, RefreshCw, Save, Search, X } from "lucide-react";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { AdminRole, AdminUser, AuditEntry, createClient, login, PagedResponse } from "../../lib/api";

type Client = ReturnType<typeof createClient>;
type Tab = "Users" | "Roles" | "Audit";
const emptyPage = { items: [], page: 1, pageSize: 20, totalCount: 0 };

export default function Administration() {
  const [client, setClient] = useState<Client | null>(null);
  const [permissions, setPermissions] = useState<string[] | null>(null);
  const [tab, setTab] = useState<Tab>("Users");
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [available, setAvailable] = useState<string[]>([]);
  const [users, setUsers] = useState<PagedResponse<AdminUser>>(emptyPage);
  const [audit, setAudit] = useState<PagedResponse<AuditEntry>>(emptyPage);
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState({ q: "", action: "", entity: "", actorId: "", fromUtc: "", toUtc: "" });
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const [editor, setEditor] = useState<{ kind: "user" | "role" | "password"; user?: AdminUser; role?: AdminRole } | null>(null);
  const [review, setReview] = useState<AuditEntry | null>(null);

  useEffect(() => {
    let active = true;
    login().then(auth => { if (active) { setClient(createClient(auth.accessToken)); setPermissions(auth.permissions); setTab(auth.permissions.includes("ManageUsers") ? "Users" : "Audit"); } })
      .catch(e => { if (active) { setError(e.message); setBusy(false); } });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!client) return;
    let active = true;
    setBusy(true); setError("");
    async function load() {
      const me = await client!.me();
      if (!active) return;
      setPermissions(me.permissions);
      if (tab === "Audit" && me.permissions.includes("ViewAudit")) {
        const query = new URLSearchParams({ page: String(page) });
        for (const [key, value] of Object.entries(filters)) if (key !== "q" && value) query.set(key, key.endsWith("Utc") ? new Date(`${value}Z`).toISOString() : value);
        const result = await client!.audit(query.toString());
        if (active) setAudit(result);
      } else if (tab !== "Audit" && me.permissions.includes("ManageUsers")) {
        const [roleList, permissionList, userPage] = await Promise.all([client!.adminRoles(), client!.adminPermissions(), client!.adminUsers(filters.q, page)]);
        if (active) { setRoles(roleList); setAvailable(permissionList); setUsers(userPage); }
      }
    }
    load().catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, [client, tab, page, filters, revision]);

  const allowed = permissions?.includes(tab === "Audit" ? "ViewAudit" : "ManageUsers");
  const result = tab === "Audit" ? audit : users;
  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setFilters({ q: String(form.get("q") ?? ""), action: String(form.get("action") ?? ""), entity: String(form.get("entity") ?? ""), actorId: String(form.get("actorId") ?? ""), fromUtc: String(form.get("fromUtc") ?? ""), toUtc: String(form.get("toUtc") ?? "") });
    setPage(1);
  }
  return <main className="admin-workspace">
    <header className="topbar"><div><Link className="secondary-link" href="/"><ArrowLeft size={16}/>Back office</Link><h1>Administration</h1></div><button className="icon-button" title="Refresh administration" aria-label="Refresh administration" onClick={() => setRevision(r => r + 1)} disabled={busy}><RefreshCw size={18}/></button></header>
    <div className="admin-tabs" role="tablist" aria-label="Administration views" onKeyDown={event => {
      if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
      event.preventDefault();
      const tabs = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>("[role=tab]"));
      const current = tabs.indexOf(event.target as HTMLButtonElement);
      const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
      tabs[next]?.click(); tabs[next]?.focus();
    }}>{(["Users", "Roles", "Audit"] as Tab[]).filter(t => permissions?.includes(t === "Audit" ? "ViewAudit" : "ManageUsers")).map(t => <button role="tab" key={t} id={`tab-${t}`} tabIndex={tab === t ? 0 : -1} aria-selected={tab === t} aria-controls="admin-content" onClick={() => { setTab(t); setPage(1); }} className={tab === t ? "selected" : ""}>{t}</button>)}</div>
    {error && <p className="sync-error" role="alert">{error}</p>}
    {busy && <p role="status">Loading administration...</p>}
    {!busy && permissions && !allowed && <p role="alert">Access denied. You do not have permission to view this section.</p>}
    {allowed && <section id="admin-content" role="tabpanel" aria-labelledby={`tab-${tab}`} aria-busy={busy}>
      <div className="panel-title"><h2>{tab}</h2>{tab !== "Audit" && <button className="primary-link" disabled={busy} onClick={() => setEditor({ kind: tab === "Users" ? "user" : "role" })}><Plus size={16}/>New {tab === "Users" ? "user" : "role"}</button>}</div>
      {tab !== "Roles" && <form className="history-filters admin-filters" onSubmit={applyFilters} key={tab}>
        {tab === "Users" ? <label>Search users<input name="q" type="search" defaultValue={filters.q}/></label> : <>
          <label>Action<input name="action" defaultValue={filters.action}/></label><label>Entity<input name="entity" defaultValue={filters.entity}/></label>
          <label>Actor UUID<input name="actorId" defaultValue={filters.actorId} pattern="[0-9a-fA-F-]{36}"/></label>
          <label>From (UTC)<input type="datetime-local" name="fromUtc" defaultValue={filters.fromUtc}/></label><label>To (UTC)<input type="datetime-local" name="toUtc" defaultValue={filters.toUtc}/></label></>}
        <button className="icon-button" title="Apply filters" aria-label="Apply filters" disabled={busy}><Search size={18}/></button>
      </form>}
      <div className="table-scroll"><table><thead><tr>{(tab === "Users" ? ["Name", "Email", "Status", "Roles", "Actions"] : tab === "Roles" ? ["Role", "Permissions", "Actions"] : ["Time (UTC)", "Actor", "Action", "Entity", "Changes"]).map(h => <th key={h} scope="col">{h}</th>)}</tr></thead><tbody>
        {tab === "Users" && users.items.map(user => <tr key={user.id}><td>{user.displayName}</td><td>{user.email}</td><td>{user.isActive ? "Active" : "Disabled"}</td><td>{user.roleIds.map(id => roles.find(r => r.id === id)?.name ?? id).join(", ") || "None"}</td><td><div className="toolbar"><button className="icon-button" title={`Edit ${user.displayName}`} aria-label={`Edit ${user.displayName}`} disabled={busy} onClick={() => setEditor({ kind: "user", user })}><Pencil size={16}/></button><button className="icon-button" title={`Reset password for ${user.displayName}`} aria-label={`Reset password for ${user.displayName}`} disabled={busy} onClick={() => setEditor({ kind: "password", user })}><KeyRound size={16}/></button></div></td></tr>)}
        {tab === "Roles" && roles.map(role => <tr key={role.id}><td>{role.name}</td><td>{role.permissions.join(", ") || "None"}</td><td><button className="icon-button" title={`Edit ${role.name}`} aria-label={`Edit ${role.name}`} disabled={busy} onClick={() => setEditor({ kind: "role", role })}><Pencil size={16}/></button></td></tr>)}
        {tab === "Audit" && audit.items.map(entry => <tr key={entry.id}><td>{new Date(entry.occurredAtUtc).toISOString().replace("T", " ").replace("Z", " UTC")}</td><td>{entry.actorName ?? entry.actorUserId ?? "System"}</td><td>{entry.action}</td><td>{entry.entityName}<small>{entry.entityId}</small></td><td><button className="icon-button" title="Review event" aria-label={`Review ${entry.action} ${entry.id}`} disabled={busy} onClick={() => setReview(entry)}><FileSearch size={16}/></button></td></tr>)}
        {!busy && !(tab === "Roles" ? roles.length : result.items.length) && <tr><td colSpan={5}>No matching records.</td></tr>}
      </tbody></table></div>
      {tab !== "Roles" && <footer className="admin-pagination"><span>{result.totalCount} records. Page {page} of {Math.max(1, Math.ceil(result.totalCount / result.pageSize))}</span><div className="toolbar"><button className="icon-button" aria-label="Previous page" title="Previous page" disabled={busy || page === 1} onClick={() => setPage(p => p - 1)}><ChevronLeft size={18}/></button><button className="icon-button" aria-label="Next page" title="Next page" disabled={busy || page * result.pageSize >= result.totalCount} onClick={() => setPage(p => p + 1)}><ChevronRight size={18}/></button></div></footer>}
    </section>}
    {editor && client && <Editor editor={editor} client={client} roles={roles} permissions={available} onClose={() => setEditor(null)} onSaved={() => { setEditor(null); setRevision(r => r + 1); }}/>} 
    {review && <AuditDialog entry={review} onClose={() => setReview(null)}/>}
  </main>;
}

function Editor({ editor, client, roles, permissions, onClose, onSaved }: { editor: { kind: "user" | "role" | "password"; user?: AdminUser; role?: AdminRole }; client: Client; roles: AdminRole[]; permissions: string[]; onClose: () => void; onSaved: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { ref.current?.showModal(); }, []);
  const title = editor.kind === "password" ? `Reset password: ${editor.user!.displayName}` : editor.kind === "role" ? (editor.role ? "Edit role" : "New role") : editor.user ? "Edit user" : "New user";
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    if (editor.kind === "user" && editor.user?.isActive && !form.has("active") && !window.confirm(`Disable ${editor.user.displayName} and revoke their sessions?`)) return;
    setBusy(true); setError("");
    try {
      if (editor.kind === "role") await client.saveRole(editor.role?.id ?? null, { name: form.get("name"), permissions: form.getAll("permissions"), version: editor.role?.version });
      else if (editor.kind === "password") await client.resetPassword(editor.user!.id, { password: form.get("password"), version: editor.user!.version });
      else await client.saveUser(editor.user?.id ?? null, { displayName: form.get("name"), email: form.get("email"), password: form.get("password"), isActive: form.has("active"), roleIds: form.getAll("roles"), version: editor.user?.version });
      onSaved();
    } catch (e) { setError(e instanceof Error ? e.message : "Save failed."); } finally { setBusy(false); }
  }
  return <dialog ref={ref} className="admin-dialog" aria-labelledby="editor-title" onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}><form onSubmit={save}>
    <header className="panel-title"><h2 id="editor-title">{title}</h2><button type="button" className="icon-button" aria-label="Close editor" title="Close editor" disabled={busy} onClick={onClose}><X size={18}/></button></header>
    {editor.kind !== "password" && <label>Name<input name="name" required maxLength={editor.kind === "role" ? 80 : 160} defaultValue={editor.user?.displayName ?? editor.role?.name} autoComplete="off"/></label>}
    {editor.kind === "user" && <><label>Email<input name="email" type="email" required maxLength={220} readOnly={!!editor.user} defaultValue={editor.user?.email} autoComplete="off"/></label>{editor.user && <label className="check-label"><input name="active" type="checkbox" defaultChecked={editor.user.isActive}/>Active</label>}</>}
    {(editor.kind === "password" || (editor.kind === "user" && !editor.user)) && <label>{editor.kind === "password" ? "New password" : "Initial password"}<input name="password" type="password" minLength={12} maxLength={128} required autoComplete="new-password"/></label>}
    {editor.kind !== "password" && <fieldset><legend>{editor.kind === "role" ? "Permissions" : "Roles"}</legend><div className="admin-checks">{(editor.kind === "role" ? permissions.map(p => ({ id: p, name: p })) : roles).map(item => <label className="check-label" key={item.id}><input type="checkbox" name={editor.kind === "role" ? "permissions" : "roles"} value={item.id} defaultChecked={editor.kind === "role" ? editor.role?.permissions.includes(item.id) : editor.user?.roleIds.includes(item.id)}/>{item.name}</label>)}</div></fieldset>}
    {error && <p className="sync-error" role="alert">{error}</p>}<button className="primary-link" disabled={busy}><Save size={16}/>{busy ? "Saving..." : editor.kind === "password" ? "Reset password" : "Save"}</button>
  </form></dialog>;
}

function AuditDialog({ entry, onClose }: { entry: AuditEntry; onClose: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { ref.current?.showModal(); }, []);
  return <dialog ref={ref} className="admin-dialog admin-audit-dialog" aria-labelledby="audit-event-title" onCancel={event => { event.preventDefault(); onClose(); }}>
    <header className="panel-title"><h2 id="audit-event-title">{entry.action}</h2><button className="icon-button" aria-label="Close audit event" title="Close audit event" onClick={onClose}><X size={18}/></button></header>
    <dl><dt>Event UUID</dt><dd>{entry.id}</dd><dt>Time (UTC)</dt><dd>{new Date(entry.occurredAtUtc).toISOString()}</dd><dt>Actor</dt><dd>{entry.actorName ?? "System"} {entry.actorUserId}</dd><dt>Entity</dt><dd>{entry.entityName} {entry.entityId}</dd><dt>Terminal UUID</dt><dd>{entry.terminalId ?? "None"}</dd><dt>Before</dt><dd><pre>{pretty(entry.beforeJson)}</pre></dd><dt>After</dt><dd><pre>{pretty(entry.afterJson)}</pre></dd></dl>
  </dialog>;
}

function pretty(json: string | null) { if (!json) return "None"; try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; } }
