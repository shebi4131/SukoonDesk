import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ChevronRight, Filter, MapPin, MessageSquarePlus, Plus, Search, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../lib/api'
import type { ComplaintCategory, ComplaintSummary } from '../lib/types'
import { humanize, shortDate } from '../lib/format'
import { useAuth } from '../auth/auth-context'

type ComplaintList = { items: ComplaintSummary[]; total: number; page: number; pageSize: number }
type Draft = { title: string; description: string; location: string; categoryId: string; priority: string }
const emptyDraft: Draft = { title: '', description: '', location: '', categoryId: '', priority: '' }

export function ComplaintsPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { hasPermission } = useAuth()
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [error, setError] = useState('')
  const query = new URLSearchParams({ ...(status && { status }), ...(search && { search }) }).toString()
  const complaints = useQuery({ queryKey: ['complaints', status, search], queryFn: () => api<ComplaintList>(`/api/complaints/?${query}`) })
  const categories = useQuery({ queryKey: ['categories'], queryFn: () => api<ComplaintCategory[]>('/api/categories/') })
  const create = useMutation({
    mutationFn: (value: Draft) => api<{ id: string }>('/api/complaints/', { method: 'POST', body: JSON.stringify({ ...value, priority: value.priority || null }) }),
    onSuccess: async (result) => { setDraft(null); await queryClient.invalidateQueries({ queryKey: ['complaints'] }); navigate(`/app/complaints/${result.id}`) },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not create complaint.'),
  })
  const canCreate = hasPermission('complaints.create')

  return <>
    <div className="page-heading"><div><p className="breadcrumb">Operations / Complaints</p><h1>Complaint workspace</h1><p>Receive, assign, and resolve every community concern from one queue.</p></div>{canCreate && <button className="button button-primary" onClick={() => setDraft(emptyDraft)}><MessageSquarePlus size={18} /> New complaint</button>}</div>
    <div className="complaint-summary-strip"><span><strong>{complaints.data?.total ?? 0}</strong> Total</span><span><strong>{complaints.data?.items.filter(x => x.isOverdue).length ?? 0}</strong> Overdue in view</span><span><strong>{complaints.data?.items.filter(x => x.status === 'InProgress').length ?? 0}</strong> In progress</span></div>
    <section className="panel table-panel">
      <div className="table-toolbar complaint-toolbar"><div className="search-box"><Search size={18} /><input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search number, title, or location…" /></div><div className="filter-select"><Filter size={16} /><select value={status} onChange={e => setStatus(e.target.value)}><option value="">All statuses</option>{['Submitted','Acknowledged','Assigned','InProgress','WaitingOnResident','Resolved','Closed','Reopened','Rejected','Cancelled'].map(x => <option value={x} key={x}>{humanize(x)}</option>)}</select></div></div>
      <div className="complaint-list-head"><span>Concern</span><span>Resident</span><span>Assigned to</span><span>Due</span><span>Status</span><span /></div>
      <div className="complaint-list">{complaints.data?.items.map(item => <button className="complaint-row" key={item.id} onClick={() => navigate(`/app/complaints/${item.id}`)}><div className="complaint-main"><i style={{ background: item.category.color }} /><span><strong>{item.title}</strong><small>{item.number} · {item.category.name}{item.location && <> · <MapPin size={11} /> {item.location}</>}</small></span>{item.priority === 'Urgent' && <em className="priority-urgent"><AlertTriangle size={12} /> Urgent</em>}</div><span>{item.resident}</span><span>{item.assignedTo ?? <em className="unassigned">Unassigned</em>}</span><span className={item.isOverdue ? 'overdue' : ''}>{shortDate(item.dueAt)}</span><span><i className={`status-chip status-${item.status.toLowerCase()}`}>{humanize(item.status)}</i></span><ChevronRight size={17} /></button>)}</div>
      {!complaints.isLoading && !complaints.data?.items.length && <div className="empty-state"><MessageSquarePlus /><h3>No complaints found</h3><p>Create the first concern or change your filters.</p>{canCreate && <button className="button button-primary" onClick={() => setDraft(emptyDraft)}><Plus size={16} /> Create complaint</button>}</div>}
    </section>
    {draft && <div className="modal-layer"><button className="drawer-scrim" onClick={() => setDraft(null)} aria-label="Close" /><div className="modal complaint-modal"><div className="modal-heading-row"><div><p className="kicker">New concern</p><h2>Create complaint</h2><p>Give the team enough detail to route and resolve it quickly.</p></div><button className="icon-button" onClick={() => setDraft(null)}><X size={19} /></button></div>{error && <div className="form-alert">{error}</div>}<label>Title<input value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })} placeholder="Briefly describe the issue" autoFocus /></label><label>Category<select value={draft.categoryId} onChange={e => setDraft({ ...draft, categoryId: e.target.value })}><option value="">Select category</option>{categories.data?.map(x => <option value={x.id} key={x.id}>{x.name}</option>)}</select></label><label>Description<textarea rows={5} value={draft.description} onChange={e => setDraft({ ...draft, description: e.target.value })} placeholder="What happened, when did it start, and who is affected?" /></label><div className="form-grid"><label>Location<input value={draft.location} onChange={e => setDraft({ ...draft, location: e.target.value })} placeholder="Block, street, or unit" /></label><label>Priority<select value={draft.priority} onChange={e => setDraft({ ...draft, priority: e.target.value })}><option value="">Category default</option>{['Low','Normal','High','Urgent'].map(x => <option key={x}>{x}</option>)}</select></label></div><div className="modal-actions"><button className="button button-ghost" onClick={() => setDraft(null)}>Cancel</button><button className="button button-primary" disabled={!draft.title || !draft.description || !draft.categoryId || create.isPending} onClick={() => create.mutate(draft)}>{create.isPending ? 'Submitting…' : 'Submit complaint'}</button></div></div></div>}
  </>
}
