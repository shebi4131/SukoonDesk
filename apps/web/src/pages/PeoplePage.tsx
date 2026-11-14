import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, CheckCircle2, Clock3, Copy, Download, Mail, MessageCircle, Search, Send, ShieldCheck, Trash2, UserCheck, UserPlus, UserRoundCog, UsersRound, X } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import type { Member, Role } from '../lib/types'
import { humanize, shortDate, whatsAppHref } from '../lib/format'
import { useAuth } from '../auth/auth-context'
import { useConfirm } from '../components/ConfirmProvider'

type PeopleTab = 'all' | 'residents' | 'team'
type StaffCredentials = { id: string; fullName: string; email: string; phoneNumber?: string; societyName: string; societySlug: string; temporaryPassword: string; message: string }

export function PeoplePage() {
  const queryClient = useQueryClient()
  const { user, hasPermission } = useAuth()
  const confirm = useConfirm()
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<PeopleTab>('all')
  const [selected, setSelected] = useState<Member | null>(null)
  const [roleIds, setRoleIds] = useState<string[]>([])
  const [message, setMessage] = useState('')
  const [staffDraft, setStaffDraft] = useState<{ fullName: string; email: string; phoneNumber: string; unitLabel: string; roleIds: string[] } | null>(null)
  const [staffCredentials, setStaffCredentials] = useState<StaffCredentials | null>(null)
  const [credentialNotice, setCredentialNotice] = useState('')
  const members = useQuery({ queryKey: ['members'], queryFn: () => api<Member[]>('/api/members/') })
  const roles = useQuery({ queryKey: ['assignable-roles'], queryFn: () => api<Role[]>('/api/members/roles'), enabled: hasPermission('users.manage') })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['members'] })
  const statusMutation = useMutation({ mutationFn: ({ id, status }: { id: string; status: string }) => api<void>(`/api/members/${id}/status`, { method: 'PUT', body: JSON.stringify({ status }) }), onSuccess: refresh })
  const rolesMutation = useMutation({ mutationFn: () => api<void>(`/api/members/${selected!.id}/roles`, { method: 'PUT', body: JSON.stringify({ roleIds }) }), onSuccess: async () => { setSelected(null); await refresh() } })
  const deleteMutation = useMutation({
    mutationFn: (id: string) => api<void>(`/api/members/${id}`, { method: 'DELETE' }),
    onSuccess: async () => { setSelected(null); setMessage('Member access was deleted. Complaint history has been preserved.'); await refresh() },
    onError: error => setMessage(error instanceof ApiError ? error.message : 'Member could not be deleted.'),
  })
  const createStaff = useMutation({
    mutationFn: () => api<StaffCredentials>('/api/members/staff', { method: 'POST', body: JSON.stringify(staffDraft) }),
    onSuccess: async result => { setStaffDraft(null); setMessage(''); setCredentialNotice('Credentials were emailed automatically.'); setStaffCredentials(result); await refresh() },
    onError: error => setMessage(error instanceof ApiError ? error.message : 'Staff registration could not be completed.'),
  })
  const emailCredentials = useMutation({
    mutationFn: () => api<{ message: string }>(`/api/members/${staffCredentials!.id}/credentials/email`, { method: 'POST', body: JSON.stringify({ temporaryPassword: staffCredentials!.temporaryPassword }) }),
    onSuccess: result => setCredentialNotice(result.message),
    onError: error => setCredentialNotice(error instanceof ApiError ? error.message : 'Email could not be sent.'),
  })
  const all = members.data ?? []
  const searched = all.filter(x => `${x.displayName} ${x.email} ${x.cnic ?? ''} ${x.phoneNumber ?? ''} ${x.whatsAppNumber ?? ''} ${x.unitLabel}`.toLowerCase().includes(search.toLowerCase()))
  const filtered = searched.filter(x => tab === 'all' || (tab === 'residents' ? x.kind === 'Resident' : x.kind === 'Team'))
  const residents = all.filter(x => x.kind === 'Resident').length
  const team = all.filter(x => x.kind === 'Team').length
  const openEditor = (member: Member) => { setMessage(''); setSelected(member); setRoleIds(member.roles.map(x => x.id)) }
  const deleteMember = async () => {
    if (!selected || !await confirm({ title: `Delete ${selected.displayName}'s access?`, message: 'They will no longer be able to sign in. Their complaint history will remain available for audit purposes.', confirmLabel: 'Delete access', danger: true })) return
    deleteMutation.mutate(selected.id)
  }
  const credentialText = (credentials: StaffCredentials) => `SukoonDesk staff access\nSociety: ${credentials.societyName}\nSociety URL: ${credentials.societySlug}\nEmail: ${credentials.email}\nTemporary password: ${credentials.temporaryPassword}\n\nPlease sign in and change your password from Profile settings.`
  const copyCredentials = async () => {
    if (!staffCredentials) return
    await navigator.clipboard.writeText(credentialText(staffCredentials))
    setCredentialNotice('Credentials copied to clipboard.')
  }
  const shareWhatsApp = () => {
    if (!staffCredentials) return
    let phone = (staffCredentials.phoneNumber ?? '').replace(/\D/g, '')
    if (phone.startsWith('0')) phone = `92${phone.slice(1)}`
    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(credentialText(staffCredentials))}`, '_blank', 'noopener,noreferrer')
  }
  const saveCredentialImage = () => {
    if (!staffCredentials) return
    const canvas = document.createElement('canvas'); canvas.width = 1200; canvas.height = 760
    const context = canvas.getContext('2d'); if (!context) return
    context.fillStyle = '#f2f7f8'; context.fillRect(0, 0, canvas.width, canvas.height)
    context.fillStyle = '#155e75'; context.fillRect(0, 0, canvas.width, 190)
    context.fillStyle = '#ffffff'; context.font = '700 54px Arial'; context.fillText('SukoonDesk', 82, 92)
    context.fillStyle = '#cffafe'; context.font = '28px Arial'; context.fillText('Staff access credentials', 82, 145)
    context.fillStyle = '#ffffff'; context.shadowColor = 'rgba(15,47,58,.16)'; context.shadowBlur = 28; context.fillRect(72, 155, 1056, 520); context.shadowBlur = 0
    context.fillStyle = '#0f2f3a'; context.font = '700 36px Arial'; context.fillText(staffCredentials.fullName, 122, 245)
    context.fillStyle = '#64748b'; context.font = '22px Arial'; context.fillText(staffCredentials.societyName, 122, 286)
    const rows = [['SOCIETY URL', staffCredentials.societySlug], ['EMAIL', staffCredentials.email], ['TEMPORARY PASSWORD', staffCredentials.temporaryPassword]]
    rows.forEach(([label, value], index) => { const y = 365 + index * 105; context.fillStyle = '#64748b'; context.font = '700 17px Arial'; context.fillText(label, 122, y); context.fillStyle = index === 2 ? '#155e75' : '#0f2f3a'; context.font = index === 2 ? '700 34px Consolas' : '700 27px Arial'; context.fillText(value, 122, y + 40) })
    context.fillStyle = '#64748b'; context.font = '18px Arial'; context.fillText('Sign in, then change this password from Profile settings.', 122, 640)
    const link = document.createElement('a'); link.download = `${staffCredentials.fullName.replace(/[^a-z0-9]/gi, '-')}-SukoonDesk-access.png`; link.href = canvas.toDataURL('image/png'); link.click()
    setCredentialNotice('Credential image saved.')
  }

  return <>
    <div className="page-heading"><div><p className="breadcrumb">Community / People</p><h1>Residents & team</h1><p>Approve registrations, register staff, and manage roles from one place.</p></div>{hasPermission('users.manage') && <button className="button button-primary" onClick={() => { setMessage(''); createStaff.reset(); setStaffDraft({ fullName: '', email: '', phoneNumber: '', unitLabel: '', roleIds: [] }) }}><UserPlus size={17} /> Register staff</button>}</div>
    {message && <div className="people-message">{message}</div>}
    <div className="people-stats"><article><UserCheck /><div><strong>{all.filter(x => x.status === 'Active').length}</strong><span>Active members</span></div></article><article><Clock3 /><div><strong>{all.filter(x => x.status === 'Pending').length}</strong><span>Awaiting approval</span></div></article><article><ShieldCheck /><div><strong>{team}</strong><span>Team members</span></div></article></div>
    <section className="panel table-panel">
      <div className="people-tabs" role="tablist"><button className={tab === 'all' ? 'active' : ''} onClick={() => setTab('all')}><UsersRound /> Everyone <span>{all.length}</span></button><button className={tab === 'residents' ? 'active' : ''} onClick={() => setTab('residents')}><UserCheck /> Residents <span>{residents}</span></button><button className={tab === 'team' ? 'active' : ''} onClick={() => setTab('team')}><ShieldCheck /> Team <span>{team}</span></button></div>
      <div className="table-toolbar"><div className="search-box"><Search size={18} /><input value={search} onChange={e => setSearch(e.target.value)} placeholder={`Search ${tab === 'all' ? 'people' : tab}…`} /></div><span>{filtered.length} shown</span></div>
      <div className="people-table-head"><span>Member</span><span>CNIC</span><span>Phone</span><span>WhatsApp</span><span>Unit</span><span>Roles</span><span>Joined</span><span>Status</span><span /></div>
      {filtered.map(member => <div className="people-row" key={member.id}><div className="person-name"><span>{member.displayName.split(' ').map(x => x[0]).slice(0,2).join('')}</span><div><strong>{member.displayName}</strong><small><Mail size={11} /> {member.email}</small></div></div><span className="member-cnic">{member.cnic || '—'}</span><span>{member.phoneNumber || '—'}</span><span>{member.whatsAppNumber ? <a className="whatsapp-link" href={whatsAppHref(member.whatsAppNumber, `Hello ${member.displayName}`)} target="_blank" rel="noreferrer"><MessageCircle size={12} /> {member.whatsAppNumber}</a> : '—'}</span><span>{member.unitLabel || '—'}</span><div className="role-pills">{member.roles.length ? member.roles.map(role => <i key={role.id}>{role.name}</i>) : <i>{member.kind}</i>}</div><span>{shortDate(member.joinedAt)}</span><span><i className={`member-status ${member.status.toLowerCase()}`}>{humanize(member.status)}</i></span><button className="icon-button" onClick={() => openEditor(member)} aria-label={`Manage ${member.displayName}`}><UserRoundCog size={17} /></button></div>)}
      {!filtered.length && <div className="empty-state compact"><UsersRound /><h3>No {tab === 'all' ? 'members' : tab} found</h3><p>Try changing the tab or search phrase.</p></div>}
    </section>
    {staffDraft && <div className="drawer-layer"><button className="drawer-scrim" onClick={() => setStaffDraft(null)} aria-label="Close" /><aside className="drawer staff-registration-drawer"><div className="drawer-header"><div><p className="kicker">Team onboarding</p><h2>Register staff member</h2></div><button className="icon-button" onClick={() => setStaffDraft(null)}><X size={19} /></button></div><div className="drawer-body"><div className="staff-invite-note"><UserPlus /><div><strong>Create secure staff access</strong><p>A temporary password and sign-in instructions will be emailed after registration.</p></div></div>{createStaff.isError && <div className="form-alert">{createStaff.error instanceof ApiError ? createStaff.error.message : 'Staff registration could not be completed.'}</div>}<section className="staff-form-section"><header><span>1</span><div><h3>Personal details</h3><p>Information used for the staff account and directory.</p></div></header><div className="staff-form-grid"><label className="full-field">Full name<input value={staffDraft.fullName} onChange={e => setStaffDraft({ ...staffDraft, fullName: e.target.value })} placeholder="Staff member's full name" autoFocus /></label><label>Email address<input type="email" value={staffDraft.email} onChange={e => setStaffDraft({ ...staffDraft, email: e.target.value })} placeholder="name@example.com" /></label><label>Phone number <small>Optional</small><input value={staffDraft.phoneNumber} onChange={e => setStaffDraft({ ...staffDraft, phoneNumber: e.target.value })} placeholder="03xx xxxxxxx" /></label><label className="full-field">Unit or department <small>Optional</small><input value={staffDraft.unitLabel} onChange={e => setStaffDraft({ ...staffDraft, unitLabel: e.target.value })} placeholder="Estate office, maintenance, security…" /></label></div></section><section className="staff-form-section"><header><span>2</span><div><h3>Access roles</h3><p>Select one or more roles. All active society roles are available.</p></div><strong>{staffDraft.roleIds.length} selected</strong></header><div className="staff-role-grid">{roles.data?.filter(role => role.isActive).map(role => <button type="button" key={role.id} className={staffDraft.roleIds.includes(role.id) ? 'selected' : ''} onClick={() => setStaffDraft({ ...staffDraft, roleIds: staffDraft.roleIds.includes(role.id) ? staffDraft.roleIds.filter(id => id !== role.id) : [...staffDraft.roleIds, role.id] })}><span className="custom-check">{staffDraft.roleIds.includes(role.id) && <Check size={14} />}</span><div><strong>{role.name}</strong><small>{role.description}</small></div></button>)}</div></section></div><div className="drawer-footer"><button className="button button-ghost" onClick={() => setStaffDraft(null)}>Cancel</button><button className="button button-primary" disabled={!staffDraft.fullName.trim() || !staffDraft.email.trim() || staffDraft.roleIds.length === 0 || createStaff.isPending} onClick={() => createStaff.mutate()}>{createStaff.isPending ? 'Registering…' : 'Register & email access'}</button></div></aside></div>}
    {staffCredentials && <div className="modal-layer credential-modal-layer"><button className="drawer-scrim" onClick={() => setStaffCredentials(null)} aria-label="Close" /><section className="modal staff-credential-modal" role="dialog" aria-modal="true"><button className="credential-close icon-button" onClick={() => setStaffCredentials(null)} aria-label="Close"><X size={19} /></button><div className="credential-success"><span><CheckCircle2 /></span><p>Staff registered successfully</p><h2>Access is ready to share</h2><small>A copy was sent to the staff member's email.</small></div><div className="credential-card"><div className="credential-person"><span>{staffCredentials.fullName.split(' ').map(x => x[0]).slice(0, 2).join('')}</span><div><strong>{staffCredentials.fullName}</strong><small>{staffCredentials.societyName}</small></div></div><dl><div><dt>Society URL</dt><dd>{staffCredentials.societySlug}</dd></div><div><dt>Email address</dt><dd>{staffCredentials.email}</dd></div><div className="credential-password"><dt>Temporary password</dt><dd>{staffCredentials.temporaryPassword}</dd></div></dl><p>Ask the staff member to change this password after signing in.</p></div>{credentialNotice && <div className="credential-notice">{credentialNotice}</div>}<div className="credential-actions"><button onClick={shareWhatsApp} disabled={!staffCredentials.phoneNumber}><MessageCircle /><span>WhatsApp</span></button><button onClick={() => emailCredentials.mutate()} disabled={emailCredentials.isPending}><Send /><span>{emailCredentials.isPending ? 'Sending…' : 'Email'}</span></button><button onClick={saveCredentialImage}><Download /><span>Save image</span></button><button onClick={copyCredentials}><Copy /><span>Copy</span></button></div><button className="button button-primary credential-done" onClick={() => setStaffCredentials(null)}>Done</button></section></div>}
    {selected && <div className="drawer-layer"><button className="drawer-scrim" onClick={() => setSelected(null)} aria-label="Close" /><aside className="drawer member-drawer"><div className="drawer-header"><div><p className="kicker">{selected.kind} access</p><h2>{selected.displayName}</h2></div><button className="icon-button" onClick={() => setSelected(null)}><X size={19} /></button></div><div className="drawer-body"><div className="member-profile"><span>{selected.displayName.slice(0,1)}</span><div><strong>{selected.displayName}</strong><small>{selected.email}</small>{selected.cnic && <small>CNIC · {selected.cnic}</small>}{selected.phoneNumber && <small>Phone · {selected.phoneNumber}</small>}{selected.whatsAppNumber && <a className="whatsapp-link" href={whatsAppHref(selected.whatsAppNumber, `Hello ${selected.displayName}`)} target="_blank" rel="noreferrer"><MessageCircle size={12} /> WhatsApp · {selected.whatsAppNumber}</a>}<small>{selected.unitLabel || 'No unit provided'}</small></div></div><div className="permission-heading"><div><strong>Account access</strong><small>Active accounts can sign in. Suspension is temporary; removal is permanent.</small></div></div>{selected.status === 'Pending' && <button className="button button-primary button-block" onClick={() => { statusMutation.mutate({ id: selected.id, status: 'Active' }); setSelected({ ...selected, status: 'Active' }) }}><CheckCircle2 size={16} /> Approve & activate account</button>}<div className="segmented-control two-options">{['Active','Suspended'].map(status => <button className={selected.status === status ? 'active' : ''} key={status} onClick={() => { statusMutation.mutate({ id: selected.id, status }); setSelected({ ...selected, status }) }}>{status === 'Suspended' ? 'Suspend temporarily' : 'Active'}</button>)}</div><div className="permission-heading"><div><strong>Assigned roles</strong><small>Choose any active society role. Staff roles move this member to the Team tab.</small></div></div><div className="role-option-list">{roles.isLoading && <div className="role-list-note">Loading roles…</div>}{roles.isError && <div className="role-list-note error">Roles could not be loaded.</div>}{roles.data?.map(role => <label key={role.id} className={!role.isActive ? 'inactive' : ''}><button type="button" disabled={!role.isActive} className={`custom-check ${roleIds.includes(role.id) ? 'checked' : ''}`} onClick={() => setRoleIds(current => current.includes(role.id) ? current.filter(x => x !== role.id) : [...current, role.id])}>{roleIds.includes(role.id) && <Check size={14} />}</button><span><strong>{role.name}{!role.isActive && <em>Inactive</em>}</strong><small>{role.description}</small></span></label>)}</div>{hasPermission('users.manage') && selected.id !== user?.membershipId && <div className="danger-zone"><div><strong>Remove member permanently</strong><small>Revokes sign-in and roles while preserving complaint history for audit.</small></div><button className="button button-danger" onClick={deleteMember} disabled={deleteMutation.isPending}><Trash2 size={16} /> {deleteMutation.isPending ? 'Removing…' : 'Remove permanently'}</button></div>}</div><div className="drawer-footer"><button className="button button-ghost" onClick={() => setSelected(null)}>Cancel</button><button className="button button-primary" onClick={() => rolesMutation.mutate()} disabled={rolesMutation.isPending}>{rolesMutation.isPending ? 'Saving…' : 'Save access'}</button></div></aside></div>}
  </>
}
