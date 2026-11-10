import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Pencil, Plus, Search, Trash2, X } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useAuth } from '../auth/auth-context'
import { useConfirm } from '../components/ConfirmProvider'

type Project = { id: string; name: string; code: string; description: string; sortOrder: number; isActive: boolean; residentCount: number }
type Draft = Partial<Project> & { name: string; code: string; description: string; sortOrder: number; isActive: boolean }
const empty: Draft = { name: '', code: '', description: '', sortOrder: 0, isActive: true }

export function ProjectsPage() {
  const queryClient = useQueryClient()
  const { hasPermission } = useAuth()
  const confirm = useConfirm()
  const [search, setSearch] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [error, setError] = useState('')
  const projects = useQuery({ queryKey: ['projects'], queryFn: () => api<Project[]>('/api/projects/?includeInactive=true') })
  const save = useMutation({
    mutationFn: (value: Draft) => api<void>(value.id ? `/api/projects/${value.id}` : '/api/projects/', { method: value.id ? 'PUT' : 'POST', body: JSON.stringify(value) }),
    onSuccess: async () => { setDraft(null); await queryClient.invalidateQueries({ queryKey: ['projects'] }) },
    onError: e => setError(e instanceof ApiError ? e.message : 'Project could not be saved.'),
  })
  const remove = useMutation({ mutationFn: (id: string) => api<void>(`/api/projects/${id}`, { method: 'DELETE' }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['projects'] }), onError: e => setError(e instanceof ApiError ? e.message : 'Project could not be deleted.') })
  const canManage = hasPermission('settings.manage')
  const rows = (projects.data ?? []).filter(x => `${x.name} ${x.code} ${x.description}`.toLowerCase().includes(search.toLowerCase()))
  const removeProject = async (project: Project) => { if (await confirm({ title: `Delete ${project.name}?`, message: 'Only unused projects can be deleted. Projects with residents should be deactivated.', confirmLabel: 'Delete project', danger: true })) remove.mutate(project.id) }
  return <>
    <div className="page-heading"><div><p className="breadcrumb">Community / Structure</p><h1>Projects & phases</h1><p>Manage the tenant-specific projects residents can select during registration.</p></div>{canManage && <button className="button button-primary" onClick={() => { setError(''); setDraft(empty) }}><Plus /> New project</button>}</div>
    {error && <div className="form-alert">{error}</div>}
    <section className="panel table-panel"><div className="table-toolbar"><div className="search-box"><Search /><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search projects…" /></div><span>{rows.length} projects</span></div><div className="project-grid">{rows.map(project => <article key={project.id}><span className="project-icon"><Building2 /></span><div><small>{project.code || 'PROJECT'}</small><h2>{project.name}</h2><p>{project.description || 'No description added.'}</p><footer><span>{project.residentCount} residents</span><i className={project.isActive ? 'active' : ''}>{project.isActive ? 'Active' : 'Inactive'}</i></footer></div>{canManage && <div className="project-actions"><button onClick={() => { setError(''); setDraft({ ...project }) }}><Pencil /></button><button disabled={project.residentCount > 0} onClick={() => removeProject(project)}><Trash2 /></button></div>}</article>)}{!rows.length && <div className="empty-state"><Building2 /><h3>No projects yet</h3><p>Add the society’s first project, phase or scheme.</p></div>}</div></section>
    {draft && <div className="drawer-layer"><button className="drawer-scrim" onClick={() => setDraft(null)} /><aside className="drawer"><div className="drawer-header"><div><p className="kicker">Tenant structure</p><h2>{draft.id ? 'Edit project' : 'Add project'}</h2></div><button className="icon-button" onClick={() => setDraft(null)}><X /></button></div><div className="drawer-body">{error && <div className="form-alert">{error}</div>}<label>Project / phase name<input value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} placeholder="e.g. Jinnah Gardens Phase I" /></label><label>Short code<input value={draft.code} onChange={e => setDraft({ ...draft, code: e.target.value })} placeholder="e.g. JG-I" /></label><label>Description<textarea rows={4} value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} /></label><label>Display order<input type="number" value={draft.sortOrder} onChange={e => setDraft({ ...draft, sortOrder: Number(e.target.value) })} /></label><label className="toggle-row"><span><strong>Active</strong><small>Available during resident registration.</small></span><input type="checkbox" checked={draft.isActive} onChange={e => setDraft({ ...draft, isActive: e.target.checked })} /></label></div><div className="drawer-footer"><button className="button button-ghost" onClick={() => setDraft(null)}>Cancel</button><button className="button button-primary" disabled={!draft.name.trim() || save.isPending} onClick={() => save.mutate(draft)}>{save.isPending ? 'Saving…' : 'Save project'}</button></div></aside></div>}
  </>
}
