import { useEffect, type FormEvent } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { ArrowLeft, Check, Eye, EyeOff } from 'lucide-react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useState } from 'react'
import { Brand } from '../components/Brand'
import { api, ApiError } from '../lib/api'
import type { CurrentUser } from '../lib/types'
import { useAuth } from '../auth/auth-context'
import { SocietyFinder, type SocietySearchResult } from '../components/SocietyFinder'

const signInSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Password is required.'),
  rememberMe: z.boolean(),
})

const signUpSchema = z.object({
  fullName: z.string().min(2, 'Enter your full name.'),
  email: z.email('Enter a valid email address.'),
  societyName: z.string().min(3, 'Enter your society name.'),
  societySlug: z.string().regex(/^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])?$/, 'Use lowercase letters, numbers or hyphens.'),
  password: z.string().min(8, 'Use at least 8 characters.').regex(/[A-Z]/, 'Add one uppercase letter.').regex(/[0-9]/, 'Add one number.'),
})

const joinSchema = z.object({
  fullName: z.string().min(2, 'Enter your full name.'),
  email: z.email('Enter a valid email address.'),
  societySlug: z.string().min(3, 'Enter the society URL.'),
  unitLabel: z.string().min(1, 'Enter your unit or property.'),
  password: z.string().min(8, 'Use at least 8 characters.').regex(/[A-Z]/, 'Add one uppercase letter.').regex(/[0-9]/, 'Add one number.'),
})

type SignInValues = z.infer<typeof signInSchema>
type SignUpValues = z.infer<typeof signUpSchema>
type JoinValues = z.infer<typeof joinSchema>

function AuthLayout({ title, intro, children, quote, storyName = 'SukoonDesk Administrator', storyRole = 'Platform administration', storyInitials = 'SA' }: { title: string; intro?: string; children: React.ReactNode; quote: string; storyName?: string; storyRole?: string; storyInitials?: string }) {
  return (
    <div className="auth-page">
      <div className="auth-panel">
        <Link to="/" className="back-link"><ArrowLeft size={17} /> Back to home</Link>
        <Brand />
        <div className="auth-form-wrap"><div className="auth-heading"><h1>{title}</h1>{intro && <p>{intro}</p>}</div>{children}</div>
        <p className="auth-legal">By continuing, you agree to our <Link to="/terms">Terms</Link> and <Link to="/privacy">Privacy Policy</Link>. <span>Developed by Codeloom.</span></p>
      </div>
      <aside className="auth-story"><div className="auth-pattern" /><div className="story-card"><div className="story-icon"><Check /></div><blockquote>“{quote}”</blockquote><div><span className="story-avatar">{storyInitials}</span><p><strong>{storyName}</strong><small>{storyRole}</small></p></div></div><p className="story-caption">Trusted workflows for thoughtful communities.</p></aside>
    </div>
  )
}

export function SignInPage() {
  const { user, completeSignIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams, setSearchParams] = useSearchParams()
  const platformMode = location.pathname === '/platform-sign-in'
  const societySlug = platformMode ? 'sukoondesk-admin' : (searchParams.get('society')?.trim().toLowerCase() ?? '')
  const [showPassword, setShowPassword] = useState(false)
  const [serverError, setServerError] = useState('')
  const [forgotOpen, setForgotOpen] = useState(false)
  const [forgotMessage, setForgotMessage] = useState('')
  const [forgotBusy, setForgotBusy] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<SignInValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: { rememberMe: true },
  })
  const { data: queriedSociety, isLoading: loadingSociety, isError: societyLookupFailed } = useQuery({
    queryKey: ['staff-sign-in-society', societySlug],
    queryFn: () => api<{ name: string; slug: string }>(`/api/public/societies/${societySlug}`),
    enabled: !!societySlug && !platformMode,
    retry: false,
  })
  const selectedSociety = platformMode ? { name: 'SukoonDesk Administration', slug: 'sukoondesk-admin' } : queriedSociety
  const invalidSociety = !platformMode && societyLookupFailed
  if (user) return <Navigate to="/app" replace />

  const selectSociety = (society: SocietySearchResult) => {
    setServerError(''); setForgotMessage(''); setForgotOpen(false)
    setSearchParams({ society: society.slug })
  }

  const changeSociety = () => {
    setServerError(''); setForgotMessage(''); setForgotOpen(false)
    setSearchParams({})
  }

  const societyStory = selectedSociety?.slug === 'fechs'
    ? { storyName: 'Wajid Naseem Sahi', storyRole: 'Secretary, FECHS', storyInitials: 'WN' }
    : { storyName: selectedSociety ? `${selectedSociety.name} Administration` : 'SukoonDesk Administrator', storyRole: selectedSociety ? 'Society support team' : 'Platform administration', storyInitials: selectedSociety?.name.split(/\s+/).map(word => word[0]).slice(0, 2).join('').toUpperCase() || 'SA' }

  const recoverPassword = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setForgotBusy(true); setServerError(''); setForgotMessage('')
    const values = Object.fromEntries(new FormData(event.currentTarget))
    try {
      const result = await api<{ message: string }>('/api/auth/forgot-password', { method: 'POST', body: JSON.stringify(values) })
      setForgotMessage(result.message)
    } catch (error) { setServerError(error instanceof ApiError ? error.message : 'The password email could not be sent.') } finally { setForgotBusy(false) }
  }

  const submit = async (values: SignInValues) => {
    setServerError('')
    try {
      await api<CurrentUser>('/api/auth/sign-in', { method: 'POST', body: JSON.stringify({ ...values, societySlug, email: values.email.trim().toLowerCase() }) })
      const authenticatedUser = await api<CurrentUser>('/api/auth/me')
      completeSignIn(authenticatedUser)
      navigate(authenticatedUser.isPlatformAdmin ? '/app/platform' : '/app', { replace: true })
    } catch (error) {
      setServerError(error instanceof ApiError ? (error.status === 401 ? 'Email or password is incorrect for this society.' : error.message) : 'We could not sign you in. Please try again.')
    }
  }

  if (!societySlug || invalidSociety) return <AuthLayout title="Find your society" intro="Search by society name, then select the correct community to continue." quote="Welcome to SukoonDesk. Find your society to open its secure staff workspace." storyName="SukoonDesk Administrator" storyRole="Platform administration" storyInitials="SA">
    {invalidSociety && <div className="form-alert">That society is unavailable. Search for an active society below.</div>}
    <SocietyFinder onSelect={selectSociety} compact />
  </AuthLayout>

  if (loadingSociety || !selectedSociety) return <AuthLayout title="Opening society" intro="Preparing the secure staff sign-in…" quote="Clear access starts with the right community workspace."><div className="auth-loading">Loading {societySlug}…</div></AuthLayout>

  if (forgotOpen) return <AuthLayout title={`${selectedSociety.name} password recovery`} intro="We will email a secure, newly generated password." quote={`Secure account recovery for ${selectedSociety.name}.`} {...societyStory}>
    <form className="auth-form" onSubmit={recoverPassword}>
      {serverError && <div className="form-alert">{serverError}</div>}{forgotMessage && <div className="form-alert success">{forgotMessage}</div>}
      <input type="hidden" name="societySlug" value={societySlug} />
      <div className="selected-society"><span>{selectedSociety.name.charAt(0)}</span><div><strong>{selectedSociety.name}</strong><small>{selectedSociety.slug}</small></div>{!platformMode && <button type="button" onClick={changeSociety}>Change</button>}</div>
      <label>Email address<input name="email" type="email" required placeholder="you@example.com" /></label>
      <button className="button button-primary button-block" disabled={forgotBusy}>{forgotBusy ? 'Sending…' : 'Email new password'}</button>
      <button type="button" className="text-button auth-back" onClick={() => { setForgotOpen(false); setServerError(''); setForgotMessage('') }}>Back to sign in</button>
    </form>
  </AuthLayout>

  return <AuthLayout title={`${selectedSociety.name} staff sign in`} intro="For society administrators, managers, supervisors, and authorized staff." quote={selectedSociety.slug === 'fechs' ? 'Our team finally has one place to see what needs attention and who owns it.' : `Welcome to ${selectedSociety.name}. Sign in to manage community concerns and follow every resolution.`} {...societyStory}>
    <form className="auth-form" onSubmit={handleSubmit(submit)}>
      {serverError && <div className="form-alert">{serverError}</div>}
      <div className="selected-society"><span>{selectedSociety.name.charAt(0)}</span><div><strong>{selectedSociety.name}</strong><small>{selectedSociety.slug}</small></div>{!platformMode && <button type="button" onClick={changeSociety}>Change</button>}</div>
      <label>Email address<input {...register('email')} type="email" placeholder="you@example.com" />{errors.email && <small className="field-error">{errors.email.message}</small>}</label>
      <label>Password<div className="password-input"><input {...register('password')} type={showPassword ? 'text' : 'password'} placeholder="Enter your password" /><button type="button" onClick={() => setShowPassword(x => !x)} aria-label="Toggle password">{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div>{errors.password && <small className="field-error">{errors.password.message}</small>}</label>
      <div className="form-options"><label className="check-label"><input type="checkbox" {...register('rememberMe')} /> Keep me signed in</label><button type="button" className="link-button" onClick={() => setForgotOpen(true)}>Forgot password?</button></div>
      <button className="button button-primary button-block" disabled={isSubmitting}>{isSubmitting ? 'Signing in…' : 'Sign in'}</button>
      <p className="form-switch">New to SukoonDesk? <Link to="/sign-up">Create your society</Link></p>
    </form>
  </AuthLayout>
}

export function SignUpPage() {
  const { user } = useAuth()
  const [showPassword, setShowPassword] = useState(false)
  const [serverError, setServerError] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const { register, handleSubmit, control, setValue, formState: { errors, isSubmitting, dirtyFields } } = useForm<SignUpValues>({ resolver: zodResolver(signUpSchema) })
  const societyName = useWatch({ control, name: 'societyName' })
  useEffect(() => {
    if (!dirtyFields.societySlug && societyName) setValue('societySlug', societyName.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '').slice(0, 40))
  }, [societyName, dirtyFields.societySlug, setValue])
  if (user) return <Navigate to="/app" replace />

  const submit = async (values: SignUpValues) => {
    setServerError('')
    try {
      await api<{ pendingApproval: boolean; message: string }>('/api/auth/sign-up', { method: 'POST', body: JSON.stringify(values) })
      setSubmitted(true)
    } catch (error) {
      setServerError(error instanceof ApiError ? error.message : 'We could not create your workspace.')
    }
  }

  return <AuthLayout title="Request a society workspace" intro="Submit your society and administrator details for approval." quote="Setup was straightforward, and the permission controls gave our committee confidence from day one.">
    {submitted ? <div className="approval-success"><Check /><h2>Request submitted</h2><p>Your society request has been sent for approval. We emailed you a confirmation and will email you again when staff sign-in is enabled.</p><Link className="button button-primary" to="/sign-in">Go to staff sign in</Link></div> : <form className="auth-form two-column-form" onSubmit={handleSubmit(submit)}>
      {serverError && <div className="form-alert full-field">{serverError}</div>}
      <label className="full-field">Society name<input {...register('societyName')} placeholder="Green View Residents Society" />{errors.societyName && <small className="field-error">{errors.societyName.message}</small>}</label>
      <label className="full-field">Society URL<div className="input-prefix"><span>sukoondesk.app/</span><input {...register('societySlug')} placeholder="green-view" /></div>{errors.societySlug && <small className="field-error">{errors.societySlug.message}</small>}</label>
      <label>Full name<input {...register('fullName')} placeholder="Your name" />{errors.fullName && <small className="field-error">{errors.fullName.message}</small>}</label>
      <label>Email address<input {...register('email')} type="email" placeholder="you@example.com" />{errors.email && <small className="field-error">{errors.email.message}</small>}</label>
      <label className="full-field">Password<div className="password-input"><input {...register('password')} type={showPassword ? 'text' : 'password'} placeholder="At least 8 characters" /><button type="button" onClick={() => setShowPassword(x => !x)}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div>{errors.password && <small className="field-error">{errors.password.message}</small>}</label>
      <button className="button button-primary button-block full-field" disabled={isSubmitting}>{isSubmitting ? 'Sending request…' : 'Submit society request'}</button>
      <p className="form-switch full-field">Already have an account? <Link to="/sign-in">Sign in</Link></p>
    </form>}
  </AuthLayout>
}

export function JoinSocietyPage() {
  const { user, completeSignIn } = useAuth()
  const navigate = useNavigate()
  const [showPassword, setShowPassword] = useState(false)
  const [serverError, setServerError] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<JoinValues>({ resolver: zodResolver(joinSchema) })
  if (user) return <Navigate to="/app" replace />
  const submit = async (values: JoinValues) => {
    setServerError('')
    try {
      const result = await api<CurrentUser | { pendingApproval: boolean; message: string }>('/api/auth/join', { method: 'POST', body: JSON.stringify(values) })
      if ('pendingApproval' in result) setSubmitted(true)
      else { completeSignIn(result); navigate('/app', { replace: true }) }
    } catch (error) { setServerError(error instanceof ApiError ? error.message : 'Registration could not be submitted.') }
  }
  return <AuthLayout title="Join your society" intro="Register as a resident using your society URL." quote="Residents know where their concern stands without chasing the office for updates.">
    {submitted ? <div className="approval-success"><Check /><h2>Registration submitted</h2><p>Your society administrator will review your membership. You can sign in after approval.</p><Link className="button button-primary" to="/sign-in">Return to sign in</Link></div> : <form className="auth-form two-column-form" onSubmit={handleSubmit(submit)}>{serverError && <div className="form-alert full-field">{serverError}</div>}<label className="full-field">Society URL<div className="input-prefix"><span>sukoondesk.app/</span><input {...register('societySlug')} placeholder="green-view" /></div>{errors.societySlug && <small className="field-error">{errors.societySlug.message}</small>}</label><label>Full name<input {...register('fullName')} placeholder="Your full name" />{errors.fullName && <small className="field-error">{errors.fullName.message}</small>}</label><label>Unit / property<input {...register('unitLabel')} placeholder="Block A · House 12" />{errors.unitLabel && <small className="field-error">{errors.unitLabel.message}</small>}</label><label className="full-field">Email<input {...register('email')} type="email" placeholder="you@example.com" />{errors.email && <small className="field-error">{errors.email.message}</small>}</label><label className="full-field">Password<div className="password-input"><input {...register('password')} type={showPassword ? 'text' : 'password'} /><button type="button" onClick={() => setShowPassword(x => !x)}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div>{errors.password && <small className="field-error">{errors.password.message}</small>}</label><button className="button button-primary button-block full-field" disabled={isSubmitting}>{isSubmitting ? 'Submitting…' : 'Submit registration'}</button><p className="form-switch full-field">Already approved? <Link to="/sign-in">Sign in</Link></p></form>}
  </AuthLayout>
}
