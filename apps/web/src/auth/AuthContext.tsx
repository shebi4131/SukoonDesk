import { useEffect, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../lib/api'
import type { CurrentUser } from '../lib/types'
import { LoadingScreen } from '../components/LoadingScreen'

import { AuthContext, type AuthValue } from './auth-context'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  useEffect(() => {
    const expireSession = () => {
      queryClient.cancelQueries()
      queryClient.removeQueries({ predicate: query => query.queryKey[0] !== 'auth' })
      queryClient.setQueryData(['auth', 'me'], null)
    }
    window.addEventListener('sukoondesk:session-expired', expireSession)
    return () => window.removeEventListener('sukoondesk:session-expired', expireSession)
  }, [queryClient])
  const me = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: () => api<CurrentUser>('/api/auth/me'),
    retry: (count, error) => !(error instanceof ApiError && error.status === 401) && count < 1,
  })

  if (me.isLoading) return <LoadingScreen />

  const user = me.error instanceof ApiError && me.error.status === 401 ? null : (me.data ?? null)
  const value: AuthValue = {
    user,
    hasPermission: (permission) => user?.permissions.includes(permission) ?? false,
    refresh: async () => { await queryClient.invalidateQueries({ queryKey: ['auth', 'me'] }) },
    completeSignIn: (authenticatedUser) => {
      queryClient.removeQueries({ predicate: query => query.queryKey[0] !== 'auth' })
      queryClient.setQueryData(['auth', 'me'], authenticatedUser)
    },
    signOut: async () => {
      await api<void>('/api/auth/sign-out', { method: 'POST' })
      await queryClient.cancelQueries()
      queryClient.removeQueries({ predicate: query => query.queryKey[0] !== 'auth' })
      queryClient.setQueryData(['auth', 'me'], null)
    },
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
