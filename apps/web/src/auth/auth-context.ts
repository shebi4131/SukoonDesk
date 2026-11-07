import { createContext, useContext } from 'react'
import type { CurrentUser } from '../lib/types'

export type AuthValue = {
  user: CurrentUser | null
  hasPermission: (permission: string) => boolean
  refresh: () => Promise<void>
  completeSignIn: (user: CurrentUser) => void
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthValue | null>(null)

export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside AuthProvider')
  return value
}
