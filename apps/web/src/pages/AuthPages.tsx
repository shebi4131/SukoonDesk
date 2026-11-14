import { useEffect, type FormEvent } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { ArrowLeft, Check, Eye, EyeOff } from 'lucide-react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { Brand } from '../components/Brand'
import { api, ApiError } from '../lib/api'
import type { CurrentUser } from '../lib/types'
import { useAuth } from '../auth/auth-context'

const signInSchema = z.object({
  identifier: z.string().min(3, 'Enter your CNIC or email address.'),
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
  const platformMode = location.pathname === '/platform-sign-in'
  const [showPassword, setShowPassword] = useState(false)
  const [serverError, setServerError] = useState('')
  const [forgotOpen, setForgotOpen] = useState(false)
  const [forgotMessage, setForgotMessage] = useState('')
  const [forgotBusy, setForgotBusy] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<SignInValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: { rememberMe: true },
  })
  if (user) return <Navigate to="/app" replace />

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
      await api<CurrentUser>('/api/auth/sign-in', { method: 'POST', body: JSON.stringify({ ...values, identifier: values.identifier.trim(), platformOnly: platformMode }) })
      const authenticatedUser = await api<CurrentUser>('/api/auth/me')
      completeSignIn(authenticatedUser)
      navigate(authenticatedUser.isPlatformAdmin ? '/app/platform' : '/app', { replace: true })
    } catch (error) {
      setServerError(error instanceof ApiError ? (error.status === 401 ? 'CNIC/email or password is incorrect, or this account does not have staff access.' : error.message) : 'We could not sign you in. Please try again.')
    }
  }

  if (forgotOpen) return <AuthLayout title="Password recovery" intro="We will email a secure, newly generated password." quote="Secure access recovery without exposing your society or account details.">
    <form className="auth-form" onSubmit={recoverPassword}>
      {serverError && <div className="form-alert">{serverError}</div>}{forgotMessage && <div className="form-alert success">{forgotMessage}</div>}
      <label>Email address<input name="email" type="email" required placeholder="you@example.com" /></label>
      <button className="button button-primary button-block" disabled={forgotBusy}>{forgotBusy ? 'Sending…' : 'Email new password'}</button>
      <button type="button" className="text-button auth-back" onClick={() => { setForgotOpen(false); setServerError(''); setForgotMessage('') }}>Back to sign in</button>
    </form>
  </AuthLayout>

  return <AuthLayout title={platformMode ? 'Platform administrator sign in' : 'Staff sign in'} intro={platformMode ? 'Secure access for SukoonDesk platform administrators.' : 'Use your CNIC or email. SukoonDesk will open the society assigned to your staff account.'} quote="One secure sign-in opens the correct community workspace automatically.">
    <form className="auth-form" onSubmit={handleSubmit(submit)}>
      {serverError && <div className="form-alert">{serverError}</div>}
      <label>CNIC or email<input {...register('identifier')} placeholder="35202-1234567-1 or you@example.com" autoComplete="username" />{errors.identifier && <small className="field-error">{errors.identifier.message}</small>}</label>
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
