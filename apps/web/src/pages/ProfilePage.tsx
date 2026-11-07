import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Eye, EyeOff, LockKeyhole, Mail, Phone, Save, UserRoundCog } from 'lucide-react'
import { api, ApiError } from '../lib/api'
import { useAuth } from '../auth/auth-context'

type Profile = { displayName: string; email: string; phoneNumber: string | null }
type Draft = Profile & { currentPassword: string; newPassword: string; confirmPassword: string }

function PasswordField({ label, value, onChange, placeholder, className }: { label: string; value: string; onChange: (value: string) => void; placeholder: string; className?: string }) {
  const [visible, setVisible] = useState(false)
  return <label className={className}>{label}<div className="password-input"><input type={visible ? 'text' : 'password'} value={value} onChange={event => onChange(event.target.value)} placeholder={placeholder} autoComplete={label.startsWith('Current') ? 'current-password' : 'new-password'} /><button type="button" onClick={() => setVisible(value => !value)} aria-label={visible ? `Hide ${label.toLowerCase()}` : `Show ${label.toLowerCase()}`}>{visible ? <EyeOff size={18} /> : <Eye size={18} />}</button></div></label>
}

export function ProfilePage() {
  const { user, refresh } = useAuth()
  const profile = useQuery({ queryKey: ['profile'], queryFn: () => api<Profile>('/api/profile/') })
  const [draft, setDraft] = useState<Draft>({ displayName: '', email: '', phoneNumber: '', currentPassword: '', newPassword: '', confirmPassword: '' })
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  useEffect(() => {
    if (profile.data) setDraft(current => ({ ...current, ...profile.data, phoneNumber: profile.data.phoneNumber ?? '' }))
  }, [profile.data])
  const save = useMutation({
    mutationFn: () => api<void>('/api/profile/', { method: 'PUT', body: JSON.stringify({ displayName: draft.displayName, email: draft.email, phoneNumber: draft.phoneNumber, currentPassword: draft.currentPassword || null, newPassword: draft.newPassword || null }) }),
    onSuccess: async () => { setError(''); setMessage('Profile and credentials updated successfully.'); setDraft(current => ({ ...current, currentPassword: '', newPassword: '', confirmPassword: '' })); await Promise.all([profile.refetch(), refresh()]) },
    onError: reason => { setMessage(''); setError(reason instanceof ApiError ? reason.message : 'Profile could not be updated.') },
  })
  const submit = () => {
    setError(''); setMessage('')
    if (user?.mustChangePassword && !draft.newPassword) { setError('You must replace the temporary password before continuing.'); return }
    if (draft.newPassword && draft.newPassword !== draft.confirmPassword) { setError('New password and confirmation do not match.'); return }
    if (draft.newPassword && draft.newPassword.length < 8) { setError('The new password must contain at least 8 characters.'); return }
    save.mutate()
  }

  if (profile.isLoading) return <div className="page-loading">Loading profile…</div>
  return <>
    <div className="page-heading"><div><p className="breadcrumb">Account / Profile</p><h1>Profile settings</h1><p>Update your personal details and secure your sign-in credentials.</p></div></div>
    <div className="profile-settings-layout">
      <aside className="panel profile-summary"><span><UserRoundCog /></span><h2>{draft.displayName || 'Your profile'}</h2><p>{draft.email}</p><small>Changes apply anywhere your account is used in SukoonDesk.</small></aside>
      <section className="panel profile-form-panel">
        {user?.mustChangePassword && <div className="form-alert">For your security, replace the temporary password before using the portal.</div>}{error && <div className="form-alert">{error}</div>}{message && <div className="form-alert success">{message}</div>}
        <div className="profile-section-heading"><span><UserRoundCog /></span><div><h2>Personal details</h2><p>Keep your contact information current.</p></div></div>
        <div className="profile-form-grid"><label>Full name<div className="profile-input"><UserRoundCog /><input value={draft.displayName} onChange={event => setDraft({ ...draft, displayName: event.target.value })} /></div></label><label>Email address<div className="profile-input"><Mail /><input type="email" value={draft.email} onChange={event => setDraft({ ...draft, email: event.target.value })} /></div></label><label className="full-field">Phone number<div className="profile-input"><Phone /><input value={draft.phoneNumber ?? ''} onChange={event => setDraft({ ...draft, phoneNumber: event.target.value })} placeholder="03xx xxxxxxx" /></div></label></div>
        <div className="profile-divider" />
        <div className="profile-section-heading"><span><LockKeyhole /></span><div><h2>Change password</h2><p>Leave these fields empty to keep your current password. Your current password is also required when changing email.</p></div></div>
        <div className="profile-form-grid"><PasswordField label="Current password" value={draft.currentPassword} onChange={value => setDraft({ ...draft, currentPassword: value })} placeholder="Required for email or password changes" /><PasswordField label="New password" value={draft.newPassword} onChange={value => setDraft({ ...draft, newPassword: value })} placeholder="At least 8 characters" /><PasswordField className="full-field" label="Confirm new password" value={draft.confirmPassword} onChange={value => setDraft({ ...draft, confirmPassword: value })} placeholder="Repeat the new password" /></div>
        <div className="profile-save"><button className="button button-primary" onClick={submit} disabled={!draft.displayName.trim() || !draft.email.trim() || save.isPending}><Save size={17} /> {save.isPending ? 'Saving…' : 'Save profile'}</button></div>
      </section>
    </div>
  </>
}
