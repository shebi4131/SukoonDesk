import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/auth-context'

export function PlatformAdminRoute() {
  const { user } = useAuth()

  if (!user?.isPlatformAdmin) return <Navigate to="/app" replace />
  return <Outlet />
}
