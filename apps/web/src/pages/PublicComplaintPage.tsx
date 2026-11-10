import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CheckCircle2, Clock3, Eye, EyeOff, FileImage, LogIn, LogOut, MapPin, MessageSquareText, Search, Send, ShieldCheck, UploadCloud, UserPlus } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Brand } from '../components/Brand'
import { SocietyFinder } from '../components/SocietyFinder'
import { api, ApiError } from '../lib/api'
import type { PublicComplaint, PublicMember, PublicSociety } from '../lib/types'
import { humanize, relativeTime } from '../lib/format'

type PublicTab = 'register' | 'sign-in' | 'new' | 'mine' | 'track'

export function PublicComplaintPage() {
  const { societySlug = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<PublicTab>('register')
  const [message, setMessage] = useState('')
  const [createdNumber, setCreatedNumber] = useState('')
  const [trackingNumber, setTrackingNumber] = useState('')
  const [tracked, setTracked] = useState<PublicComplaint | null>(null)
  const [busy, setBusy] = useState(false)
  const [showRecovery, setShowRecovery] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const { data: society, isError: societyUnavailable } = useQuery({ queryKey: ['public-society', societySlug], queryFn: () => api<PublicSociety>(`/api/public/societies/${societySlug}`), enabled: !!societySlug, retry: false })
  const { data: memberSession } = useQuery({ queryKey: ['public-member'], queryFn: () => api<PublicMember>('/api/public/me'), retry: false })
  const member = memberSession?.tenant.slug === societySlug ? memberSession : undefined
  const { data: complaints = [] } = useQuery({ queryKey: ['public-complaints'], queryFn: () => api<Array<Pick<PublicComplaint, 'number' | 'title' | 'status' | 'priority' | 'location' | 'createdAt' | 'updatedAt' | 'dueAt' | 'category'>>>('/api/public/complaints'), enabled: !!member })

  const visibleTab: PublicTab = member && (tab === 'register' || tab === 'sign-in') ? 'mine' : tab
  useEffect(() => {
    if (!member || (tab !== 'register' && tab !== 'sign-in')) return
    const timer = window.setTimeout(() => setTab('mine'), 0)
    return () => window.clearTimeout(timer)
  }, [member, tab])

  if (!societySlug || societyUnavailable) return <div className="public-desk public-gate"><div className="public-gate-card"><Brand /><h1>Find your society complaint desk</h1><p>Search by society name and select the correct community to register or track a complaint.</p>{societyUnavailable && <div className="public-message">That society is unavailable. Please choose an active society.</div>}<SocietyFinder onSelect={selected => navigate(`/public/${selected.slug}`)} compact /><Link to="/" className="back-link"><ArrowLeft size={16} /> Back to SukoonDesk</Link></div></div>

  const register = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setMessage('')
    const form = new FormData(event.currentTarget)
    const values = {
      fullName: String(form.get('fullName') ?? '').trim(),
      cnic: String(form.get('cnic') ?? '').trim(),
      whatsAppNumber: String(form.get('whatsAppNumber') ?? '').trim(),
      email: String(form.get('email') ?? '').trim(),
      password: String(form.get('password') ?? ''),
      plotNo: String(form.get('plotNo') ?? '').trim(),
      streetNo: String(form.get('streetNo') ?? '').trim(),
      block: String(form.get('block') ?? '').trim(),
      projectId: String(form.get('projectId') ?? ''),
      notifyByEmail: form.get('notifyByEmail') === 'on',
      notifyByWhatsApp: form.get('notifyByWhatsApp') === 'on',
    }
    const problems: string[] = []
    if (values.fullName.length < 2) problems.push('Enter the member’s full name.')
    if (values.cnic.replace(/\D/g, '').length !== 13) problems.push('CNIC must contain exactly 13 digits.')
    if (values.whatsAppNumber.replace(/\D/g, '').length < 10) problems.push('Enter a valid WhatsApp number.')
    if (values.password.length < 8 || !/[A-Z]/.test(values.password) || !/\d/.test(values.password)) problems.push('Password must contain at least 8 characters, one uppercase letter and one number.')
    if (!values.plotNo || !values.streetNo) problems.push('Plot number and street number are required.')
    if (!values.projectId) problems.push('Select your project or phase.')
    if (!values.notifyByEmail && !values.notifyByWhatsApp) problems.push('Select at least one update channel.')
    if (problems.length) { setMessage(problems.join(' ')); return }
    setBusy(true)
    try {
      await api(`/api/public/societies/${societySlug}/register`, { method: 'POST', body: JSON.stringify(values) })
      await queryClient.invalidateQueries({ queryKey: ['public-member'] }); setTab('new'); setMessage('Your member account is ready. You can now register a complaint.')
    } catch (error) {
      const details = error instanceof ApiError ? Object.values(error.details ?? {}).flat() : []
      setMessage(details.length ? details.join(' ') : error instanceof ApiError ? error.message : 'Registration could not be completed.')
    } finally { setBusy(false) }
  }
  const signIn = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setMessage('')
    const values = Object.fromEntries(new FormData(event.currentTarget))
    try { await api(`/api/public/societies/${societySlug}/sign-in`, { method: 'POST', body: JSON.stringify(values) }); await queryClient.invalidateQueries({ queryKey: ['public-member'] }); setTab('mine') }
    catch { setMessage('CNIC or password is incorrect.') } finally { setBusy(false) }
  }
  const signOut = async () => { await api('/api/public/sign-out', { method: 'POST' }); queryClient.clear(); setTracked(null); setTab('sign-in') }
  const recoverPassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setMessage('')
    const email = String(new FormData(event.currentTarget).get('email') ?? '')
    try { const result = await api<{ message: string }>('/api/auth/forgot-password', { method: 'POST', body: JSON.stringify({ societySlug, email }) }); setMessage(result.message) }
    catch (error) { setMessage(error instanceof ApiError ? error.message : 'The password email could not be sent.') } finally { setBusy(false) }
  }
  const createComplaint = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setMessage(''); const form = event.currentTarget; const values = new FormData(form); const files = values.getAll('images').filter(x => x instanceof File && x.size) as File[]
    const payload = { title: values.get('title'), description: values.get('description'), location: values.get('location'), categoryId: values.get('categoryId'), priority: values.get('priority') || null }
    try {
      const created = await api<{ number: string }>('/api/public/complaints', { method: 'POST', body: JSON.stringify(payload) })
      form.reset(); setCreatedNumber(created.number); setTrackingNumber(created.number); await queryClient.invalidateQueries({ queryKey: ['public-complaints'] })
      let failedUploads = 0
      for (const file of files) {
        const body = new FormData(); body.append('file', file)
        try { await api(`/api/public/complaints/${created.number}/attachments`, { method: 'POST', body }) }
        catch { failedUploads += 1 }
      }
      if (failedUploads) setMessage(`Complaint ${created.number} was registered, but ${failedUploads} image${failedUploads === 1 ? '' : 's'} could not be uploaded. You can add evidence from the complaint page.`)
    } catch (error) { setMessage(error instanceof ApiError ? error.message : 'Complaint could not be registered.') } finally { setBusy(false) }
  }
  const loadComplaint = async (number = trackingNumber) => {
    setBusy(true); setMessage('')
    try { const result = await api<PublicComplaint>(`/api/public/complaints/${number.trim().toUpperCase()}`); setTracked(result); setTrackingNumber(result.number); setTab('track') }
    catch { setTracked(null); setMessage('No complaint with that number was found in your account.') } finally { setBusy(false) }
  }
  const reply = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!tracked) return; setBusy(true); const form = event.currentTarget; const values = new FormData(form); const body = String(values.get('body') ?? ''); const file = values.get('image')
    try {
      const result = await api<{ id: string }>(`/api/public/complaints/${tracked.number}/comments`, { method: 'POST', body: JSON.stringify({ body }) })
      if (file instanceof File && file.size) { const upload = new FormData(); upload.append('file', file); await api(`/api/public/complaints/${tracked.number}/comments/${result.id}/attachments`, { method: 'POST', body: upload }) }
      form.reset(); await loadComplaint(tracked.number); setMessage('Your update was posted successfully.')
    } catch (error) { setMessage(error instanceof ApiError ? error.message : 'Reply could not be sent.') } finally { setBusy(false) }
  }

  return <div className="public-desk">
    <header className="public-nav"><Brand /><div><span><ShieldCheck size={15} /> Secure complaint desk</span>{member && <><strong>{member.fullName}</strong><button onClick={signOut}><LogOut size={15} /> Sign out</button></>}</div></header>
    <section className="public-hero"><div><span className="kicker">{society?.name ?? 'Community service desk'}</span><h1>Tell us what needs attention.</h1><p>Register a concern, attach photos, and follow every update with your unique complaint number.</p><button className="button public-track-cta" onClick={() => { if (member) setTab('track'); else { setTab('sign-in'); setMessage('Sign in with your member account to securely track a complaint.') } }}><Search size={17} /> Track complaint</button></div><div className="public-trust"><span><CheckCircle2 /> Trackable</span><span><Clock3 /> SLA monitored</span><span><ShieldCheck /> Private</span></div></section>
    <main className="public-shell">
      <nav className="public-tabs">{member ? <><button className={visibleTab === 'new' ? 'active' : ''} onClick={() => setTab('new')}><Send /> New complaint</button><button className={visibleTab === 'mine' ? 'active' : ''} onClick={() => setTab('mine')}><MessageSquareText /> My complaints</button><button className={visibleTab === 'track' ? 'active' : ''} onClick={() => setTab('track')}><Search /> Track complaint</button></> : <><button className={visibleTab === 'register' ? 'active' : ''} onClick={() => setTab('register')}><UserPlus /> Register</button><button className={visibleTab === 'sign-in' ? 'active' : ''} onClick={() => setTab('sign-in')}><LogIn /> Member sign in & track</button></>}</nav>
      {message && <div className={`public-message ${createdNumber ? 'success' : ''}`}>{message}</div>}
      {!member && tab === 'register' && <section className="public-card"><div className="public-card-heading"><span><UserPlus /></span><div><h2>Register as a society member</h2><p>Your information is saved securely so you can sign in and track future complaints.</p></div></div><form className="public-form" onSubmit={register}>
        <label className="wide">Full name<input name="fullName" required minLength={2} autoComplete="name" placeholder="As shown on CNIC" /></label><label>CNIC<input name="cnic" required inputMode="numeric" maxLength={15} placeholder="35202-1234567-1" /></label><label>WhatsApp number<input name="whatsAppNumber" required inputMode="tel" autoComplete="tel" placeholder="03xx xxxxxxx" /></label><label>Email address<input name="email" type="email" required autoComplete="email" placeholder="you@example.com" /></label><label>Password<div className="password-input"><input name="password" type={showPassword ? 'text' : 'password'} required minLength={8} autoComplete="new-password" placeholder="8+ chars, uppercase & number" /><button type="button" onClick={() => setShowPassword(value => !value)} aria-label={showPassword ? 'Hide password' : 'Show password'} title={showPassword ? 'Hide password' : 'Show password'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div></label>
        <div className="form-divider wide"><span>Property address</span></div><label>Plot number<input name="plotNo" required placeholder="124" /></label><label>Street number<input name="streetNo" required placeholder="12" /></label><label>Block <small>Optional</small><input name="block" placeholder="A" /></label><label>Project / phase<select name="projectId" required><option value="">Select project</option>{society?.projects.map(project => <option key={project.id} value={project.id}>{project.code ? `${project.code} — ` : ''}{project.name}</option>)}</select></label><div className="preference-card wide"><div><strong>Complaint updates</strong><small>Email is enabled by default. WhatsApp updates are optional where supported by your society.</small></div><label><span>Email</span><input name="notifyByEmail" type="checkbox" defaultChecked /></label><label><span>WhatsApp</span><input name="notifyByWhatsApp" type="checkbox" /></label></div><button className="button button-primary wide" disabled={busy}>{busy ? 'Creating account…' : 'Register member account'}</button>
      </form></section>}
      {!member && tab === 'sign-in' && <section className="public-card public-auth-card"><div className="public-card-heading"><span><LogIn /></span><div><h2>{showRecovery ? 'Recover your password' : 'Welcome back'}</h2><p>{showRecovery ? 'Enter the email used during member registration.' : 'Sign in with the CNIC and password used during registration.'}</p></div></div>{showRecovery ? <form className="public-form" onSubmit={recoverPassword}><label className="wide">Email address<input name="email" type="email" required placeholder="you@example.com" /></label><button className="button button-primary wide" disabled={busy}>{busy ? 'Sending…' : 'Email new password'}</button><button type="button" className="text-button wide" onClick={() => setShowRecovery(false)}>Back to member sign in</button></form> : <form className="public-form" onSubmit={signIn}><label className="wide">CNIC<input name="cnic" required placeholder="35202-1234567-1" /></label><label className="wide">Password<div className="password-input"><input name="password" type={showPassword ? 'text' : 'password'} required /><button type="button" onClick={() => setShowPassword(value => !value)} aria-label={showPassword ? 'Hide password' : 'Show password'} title={showPassword ? 'Hide password' : 'Show password'}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div></label><button className="button button-primary wide" disabled={busy}>Sign in to complaint desk</button><button type="button" className="text-button wide" onClick={() => setShowRecovery(true)}>Forgot password?</button></form>}</section>}
      {member && tab === 'new' && <section className="public-card"><div className="public-card-heading"><span><Send /></span><div><h2>Register a new complaint</h2><p>Clear details and photos help the right team resolve your concern faster.</p></div></div>{createdNumber ? <div className="tracking-success"><CheckCircle2 /><span>Complaint registered successfully</span><strong>{createdNumber}</strong><p>Save this number. It is your permanent reference for updates and replies.</p><div><button className="button button-primary" onClick={() => loadComplaint(createdNumber)}>View complaint</button><button className="button button-ghost" onClick={() => setCreatedNumber('')}>Register another</button></div></div> : <form className="public-form" onSubmit={createComplaint}><label className="wide">Complaint title<input name="title" required placeholder="Briefly describe the concern" /></label><label>Category<select name="categoryId" required><option value="">Select category</option>{society?.categories.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Priority<select name="priority"><option value="">Category default</option><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label><label className="wide">Description<textarea name="description" required rows={5} placeholder="What happened, when did it start, and who is affected?" /></label><label className="wide">Location <small>Defaults to your registered property</small><input name="location" placeholder={`${member.plotNo}, Street ${member.streetNo}`} /></label><label className="upload-field wide"><UploadCloud /><span><strong>Add evidence photos</strong><small>JPG, PNG or WebP · up to 8 MB each</small></span><input name="images" type="file" accept="image/jpeg,image/png,image/webp" multiple /></label><button className="button button-primary wide" disabled={busy}>{busy ? 'Registering complaint…' : 'Register complaint & get number'}</button></form>}</section>}
      {member && tab === 'mine' && <section className="public-card"><div className="public-card-heading"><span><MessageSquareText /></span><div><h2>My complaints</h2><p>Every concern registered using your member account.</p></div></div><div className="public-complaint-list">{complaints.map(x => <button key={x.number} onClick={() => loadComplaint(x.number)}><i className={`status-dot status-${x.status.toLowerCase()}`} /><span><strong>{x.title}</strong><small>{x.number} · {x.category} · {relativeTime(x.updatedAt)}</small></span><em className={`status-chip status-${x.status.toLowerCase()}`}>{humanize(x.status)}</em></button>)}{complaints.length === 0 && <div className="public-empty"><MessageSquareText /><h3>No complaints yet</h3><p>Your registered complaints will appear here.</p></div>}</div></section>}
      {member && tab === 'track' && <><section className="public-track-bar"><label>Complaint number<input value={trackingNumber} onChange={e => setTrackingNumber(e.target.value.toUpperCase())} placeholder="CMP-2026-123456" /></label><button className="button button-primary" onClick={() => loadComplaint()} disabled={busy || !trackingNumber}>Track complaint <Search size={16} /></button></section>{tracked && <section className="public-detail"><header><div><span>{tracked.number}</span><h2>{tracked.title}</h2><p><MapPin /> {tracked.location} · {tracked.category}</p></div><em className={`status-chip status-${tracked.status.toLowerCase()}`}>{humanize(tracked.status)}</em></header><div className="public-detail-grid"><div><section><h3>Complaint details</h3><p>{tracked.description}</p>{tracked.attachments.length > 0 && <div className="attachment-gallery">{tracked.attachments.map(x => <a key={x.id} href={x.url} target="_blank"><img src={x.url} alt={x.fileName} /></a>)}</div>}</section><section><h3>Updates & replies</h3><div className="public-conversation">{tracked.comments.map(x => <article key={x.id}><strong>{x.author}</strong><time>{relativeTime(x.createdAt)}</time><p>{x.body}</p>{x.attachments.map(a => <a key={a.id} href={a.url} target="_blank"><FileImage /> {a.fileName}</a>)}</article>)}</div><form className="public-reply" onSubmit={reply}><textarea name="body" required placeholder="Add information or reply to the society team" /><label><FileImage /> Attach image<input name="image" type="file" accept="image/jpeg,image/png,image/webp" /></label><button className="button button-primary" disabled={busy}>{busy ? 'Posting…' : 'Post update'}</button></form></section></div><aside><h3>Progress timeline</h3>{tracked.history.map((x, index) => <div className="public-timeline" key={`${x.createdAt}-${index}`}><span /><div><strong>{humanize(x.toStatus)}</strong><p>{x.note}</p><small>{relativeTime(x.createdAt)}</small></div></div>)}</aside></div></section>}</>}
    </main><footer className="public-footer"><Brand /><span>Private, accountable complaint handling for {society?.name}.</span><span><Link to="/terms">Terms</Link> · <Link to="/privacy">Privacy</Link> · Developed by Codeloom</span></footer>
  </div>
}
