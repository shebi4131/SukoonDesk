import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, LockKeyhole, Pencil, Plus, Search, Shield, Trash2, X } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import type { Permission, Role } from '../lib/types'
import { useAuth } from '../auth/auth-context'

type RoleDraft = { id?: string; name: string; description: string; isActive: boolean; permissionKeys: string[]; isSystem?: boolean }
const emptyRole: RoleDraft = { name: '', description: '', isActive: true, permissionKeys: [] }

export function RolesPage() {
  const queryClient = useQueryClient()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('roles.manage')
  const [search, setSearch] = useState('')
  const [draft, setDraft] = useState<RoleDraft | null>(null)
  const [error, setError] = useState('')
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/rbac/roles') })
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => api<Permission[]>('/api/rbac/permissions') })
  const save = useMutation({
    mutationFn: (value: RoleDraft) => api<void>(value.id ? `/api/rbac/roles/${value.id}` : '/api/rbac/roles', { method: value.id ? 'PUT' : 'POST', body: JSON.stringify(value) }),
    onSuccess: async () => { setDraft(null); await queryClient.invalidateQueries({ queryKey: ['roles'] }) },
    onError: (e) => setError(e instanceof ApiError ? e.message : 'Could not save this role.'),
  })
  const remove = useMutation({ mutationFn: (id: string) => api<void>(`/api/rbac/roles/${id}`, { method: 'DELETE' }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['roles'] }) })
  const groups = useMemo(() => (permissions.data ?? []).reduce<Record<string, Permission[]>>((result, item) => {
    ;(result[item.group] ??= []).push(item)
    return result
  }, {}), [permissions.data])
  const filtered = (roles.data ?? []).filter(role => `${role.name} ${role.description}`.toLowerCase().includes(search.toLowerCase()))
  const togglePermission = (key: string) => setDraft(current => current ? ({ ...current, permissionKeys: current.permissionKeys.includes(key) ? current.permissionKeys.filter(x => x !== key) : [...current.permissionKeys, key] }) : current)

  return <>
    <div className="page-heading"><div><p className="breadcrumb">Administration / Access control</p><h1>Roles & permissions</h1><p>Control what residents, supervisors, and administrators can see and do.</p></div>{canManage && <button className="button button-primary" onClick={() => setDraft(emptyRole)}><Plus size={18} /> New role</button>}</div>
    <section className="panel table-panel"><div className="table-toolbar"><div className="search-box"><Search size={18} /><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search roles…" /></div><span>{filtered.length} roles</span></div><div className="role-table"><div className="role-table-head"><span>Role</span><span>Members</span><span>Permissions</span><span>Status</span><span /></div>{filtered.map(role => <div className="role-table-row" key={role.id}><div className="role-name"><span className={`role-icon ${role.isSystem ? 'system' : ''}`}>{role.isSystem ? <LockKeyhole size={18} /> : <Shield size={18} />}</span><span><strong>{role.name}</strong><small>{role.description}</small></span>{role.isSystem && <em>System</em>}</div><span>{role.memberCount}</span><span>{role.permissionKeys.length} permissions</span><span><i className={`status-badge ${role.isActive ? 'active' : ''}`}>{role.isActive ? 'Active' : 'Inactive'}</i></span><div className="row-actions">{canManage && <><button onClick={() => setDraft({ ...role })}><Pencil size={17} /></button>{!role.isSystem && <button onClick={() => remove.mutate(role.id)}><Trash2 size={17} /></button>}</>}</div></div>)}</div></section>
    {draft && <div className="drawer-layer"><button className="drawer-scrim" onClick={() => setDraft(null)} aria-label="Close editor" /><aside className="drawer"><div className="drawer-header"><div><p className="kicker">Access control</p><h2>{draft.id ? 'Edit role' : 'Create role'}</h2></div><button className="icon-button" onClick={() => setDraft(null)}><X size={20} /></button></div><div className="drawer-body">{error && <div className="form-alert">{error}</div>}<label>Role name<input value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} placeholder="e.g. Maintenance Supervisor" /></label><label>Description<textarea value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} rows={3} /></label><div className="permission-heading"><div><strong>Permissions</strong><small>{draft.permissionKeys.length} selected</small></div><button className="text-button" onClick={() => setDraft({ ...draft, permissionKeys: permissions.data?.map(x => x.key) ?? [] })}>Select all</button></div><div className="permission-groups">{Object.entries(groups).map(([group, items]) => <section key={group}><h3>{group}</h3>{items?.map(permission => <label className="permission-row" key={permission.key}><button type="button" className={`custom-check ${draft.permissionKeys.includes(permission.key) ? 'checked' : ''}`} onClick={() => togglePermission(permission.key)}>{draft.permissionKeys.includes(permission.key) && <Check size={14} />}</button><span><strong>{permission.name}</strong><small>{permission.description}</small></span></label>)}</section>)}</div></div><div className="drawer-footer"><button className="button button-ghost" onClick={() => setDraft(null)}>Cancel</button><button className="button button-primary" disabled={!draft.name.trim() || save.isPending} onClick={() => save.mutate(draft)}>{save.isPending ? 'Saving…' : 'Save role'}</button></div></aside></div>}
  </>
}
