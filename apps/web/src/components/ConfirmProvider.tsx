import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { AlertTriangle, HelpCircle, X } from 'lucide-react'

type ConfirmOptions = {
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
}

type PendingConfirmation = ConfirmOptions & { resolve: (accepted: boolean) => void }
const ConfirmContext = createContext<(options: ConfirmOptions) => Promise<boolean>>(() => Promise.resolve(false))

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirmation | null>(null)
  const confirmButton = useRef<HTMLButtonElement>(null)
  const confirm = useCallback((options: ConfirmOptions) => new Promise<boolean>(resolve => setPending({ ...options, resolve })), [])
  const close = useCallback((accepted: boolean) => {
    setPending(current => {
      current?.resolve(accepted)
      return null
    })
  }, [])

  useEffect(() => {
    if (!pending) return
    confirmButton.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [pending, close])

  return <ConfirmContext.Provider value={confirm}>
    {children}
    {pending && <div className="confirm-layer" role="presentation">
      <button className="confirm-scrim" onClick={() => close(false)} aria-label="Cancel confirmation" />
      <section className={`confirm-dialog ${pending.danger ? 'danger' : ''}`} role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message">
        <button className="confirm-close" onClick={() => close(false)} aria-label="Close"><X size={18} /></button>
        <span className="confirm-symbol">{pending.danger ? <AlertTriangle /> : <HelpCircle />}</span>
        <div className="confirm-copy"><p>Confirmation required</p><h2 id="confirm-title">{pending.title}</h2><span id="confirm-message">{pending.message}</span></div>
        <div className="confirm-actions"><button className="button button-ghost" onClick={() => close(false)}>{pending.cancelLabel ?? 'Cancel'}</button><button ref={confirmButton} className={`button ${pending.danger ? 'button-danger' : 'button-primary'}`} onClick={() => close(true)}>{pending.confirmLabel ?? 'Confirm'}</button></div>
      </section>
    </div>}
  </ConfirmContext.Provider>
}

export const useConfirm = () => useContext(ConfirmContext)
