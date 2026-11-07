import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, CirclePause, CirclePlay, Search, ShieldCheck, TicketCheck, UsersRound } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useConfirm } from '../components/ConfirmProvider'
import { shortDate } from '../lib/format'

type PlatformTenant = { id: string; name: string; slug: string; isActive: boolean; createdAt: string; memberCount: number; complaintCount: number; openComplaintCount: number; administrator: { displayName: string; email: string } | null }
type PlatformDashboard = { totalTenants: number; activeTenants: number; suspendedTenants: number; totalMembers: number; totalComplaints: number; openComplaints: number; tenants: PlatformTenant[] }

export function PlatformAdminPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [search, setSearch] = useState('')
  const [message, setMessage] = useState('')
  const dashboard = useQuery({ queryKey: ['platform-dashboard'], queryFn: () => api<PlatformDashboard>('/api/platform/dashboard') })
  const status = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => api<{ message: string }>(`/api/platform/tenants/${id}/status`, { method: 'PUT', body: JSON.stringify({ isActive }) }),
    onSuccess: async result => { setMessage(result.message); await queryClient.invalidateQueries({ queryKey: ['platform-dashboard'] }) },
    onError: error => setMessage(error instanceof ApiError ? error.message : 'Society status could not be updated.'),
  })
  const changeStatus = async (tenant: PlatformTenant) => {
    const activate = !tenant.isActive
    if (!await confirm({ title: `${activate ? 'Activate' : 'Suspend'} ${tenant.name}?`, message: activate ? 'Its administrators, staff, and residents will regain access.' : 'All society sign-ins and active sessions will be blocked until it is activated again.', confirmLabel: activate ? 'Activate society' : 'Suspend society', danger: !activate })) return
    status.mutate({ id: tenant.id, isActive: activate })
  }
  const tenants = (dashboard.data?.tenants ?? []).filter(x => `${x.name} ${x.slug} ${x.administrator?.displayName ?? ''} ${x.administrator?.email ?? ''}`.toLowerCase().includes(search.toLowerCase()))

  if (dashboard.isLoading) return <div className="page-loading">Loading platform administration…</div>
  if (dashboard.isError) return <div className="form-alert">Platform administration is unavailable or your account is not authorized.</div>
  return <>
    <div className="page-heading platform-heading"><div><p className="breadcrumb">SukoonDesk / Platform administration</p><h1>Tenant command center</h1><p>Monitor adoption and control society access from one concise workspace.</p></div><a className="button button-ghost" href="/app/profile"><ShieldCheck size={17} /> Administrator profile</a></div>
    {message && <div className="people-message">{message}</div>}
    <div className="platform-metrics"><article><span><Building2 /></span><div><strong>{dashboard.data?.totalTenants ?? 0}</strong><small>Total societies</small></div><em>{dashboard.data?.activeTenants ?? 0} active</em></article><article><span><UsersRound /></span><div><strong>{dashboard.data?.totalMembers ?? 0}</strong><small>Registered people</small></div></article><article><span><TicketCheck /></span><div><strong>{dashboard.data?.totalComplaints ?? 0}</strong><small>All complaints</small></div><em>{dashboard.data?.openComplaints ?? 0} open</em></article><article className="warning"><span><CirclePause /></span><div><strong>{dashboard.data?.suspendedTenants ?? 0}</strong><small>Pending / suspended</small></div></article></div>
    <section className="panel platform-tenants"><header><div><h2>Societies</h2><p>Access changes apply immediately to sign-ins and at active-session validation.</p></div><div className="search-box"><Search size={17} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Search societies…" /></div></header><div className="platform-tenant-head"><span>Society</span><span>Administrator</span><span>Members</span><span>Complaints</span><span>Created</span><span>Status</span><span /></div>{tenants.map(tenant => <article className="platform-tenant-row" key={tenant.id}><div className="platform-society"><span>{tenant.name.slice(0,1)}</span><div><strong>{tenant.name}</strong><small>{tenant.slug}</small></div></div><div className="platform-admin"><strong>{tenant.administrator?.displayName ?? 'Not assigned'}</strong><small>{tenant.administrator?.email ?? '—'}</small></div><strong>{tenant.memberCount}</strong><div><strong>{tenant.complaintCount}</strong><small>{tenant.openComplaintCount} open</small></div><span>{shortDate(tenant.createdAt)}</span><i className={`tenant-state ${tenant.isActive ? 'active' : 'suspended'}`}>{tenant.isActive ? 'Active' : 'Suspended'}</i><button className={`tenant-status-button ${tenant.isActive ? 'suspend' : 'activate'}`} disabled={status.isPending} onClick={() => changeStatus(tenant)}>{tenant.isActive ? <CirclePause /> : <CirclePlay />}<span>{tenant.isActive ? 'Suspend' : 'Activate'}</span></button></article>)}{!tenants.length && <div className="empty-state compact"><Building2 /><h3>No societies found</h3><p>Try a different search phrase.</p></div>}</section>
  </>
}
