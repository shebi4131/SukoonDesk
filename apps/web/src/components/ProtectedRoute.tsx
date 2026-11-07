import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/auth-context'

export function ProtectedRoute() {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) return <Navigate to="/sign-in" replace state={{ from: location.pathname }} />
  if (user.mustChangePassword && location.pathname !== '/app/profile') return <Navigate to="/app/profile" replace state={{ forcePasswordChange: true }} />
  return <Outlet />
}
