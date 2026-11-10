import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppShell } from './components/AppShell'
import { LandingPage } from './pages/LandingPage'
import { SignInPage, SignUpPage } from './pages/AuthPages'
import { DashboardPage } from './pages/DashboardPage'
import { RolesPage } from './pages/RolesPage'
import { SettingsPage } from './pages/SettingsPage'
import { ComplaintsPage } from './pages/ComplaintsPage'
import { ComplaintDetailPage } from './pages/ComplaintDetailPage'
import { PeoplePage } from './pages/PeoplePage'
import { ReportsPage } from './pages/ReportsPage'
import { CategoriesPage } from './pages/CategoriesPage'
import { PublicComplaintPage } from './pages/PublicComplaintPage'
import { ConfirmProvider } from './components/ConfirmProvider'
import { ProfilePage } from './pages/ProfilePage'
import { PlatformAdminPage } from './pages/PlatformAdminPage'
import { PrivacyPage, TermsPage } from './pages/LegalPages'
import { PricingPage } from './pages/PricingPage'
import { ProjectsPage } from './pages/ProjectsPage'

const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, refetchOnWindowFocus: false } } })

export default function App() {
  return <QueryClientProvider client={queryClient}><BrowserRouter><ConfirmProvider><AuthProvider><Routes><Route path="/" element={<LandingPage />} /><Route path="/pricing" element={<PricingPage />} /><Route path="/terms" element={<TermsPage />} /><Route path="/privacy" element={<PrivacyPage />} /><Route path="/public" element={<PublicComplaintPage />} /><Route path="/public/:societySlug" element={<PublicComplaintPage />} /><Route path="/sign-in" element={<SignInPage />} /><Route path="/platform-sign-in" element={<SignInPage />} /><Route path="/sign-up" element={<SignUpPage />} /><Route path="/join" element={<Navigate to="/public" replace />} /><Route element={<ProtectedRoute />}><Route path="/app" element={<AppShell />}><Route index element={<DashboardPage />} /><Route path="complaints" element={<ComplaintsPage />} /><Route path="complaints/:id" element={<ComplaintDetailPage />} /><Route path="people" element={<PeoplePage />} /><Route path="reports" element={<ReportsPage />} /><Route path="categories" element={<CategoriesPage />} /><Route path="projects" element={<ProjectsPage />} /><Route path="roles" element={<RolesPage />} /><Route path="settings" element={<SettingsPage />} /><Route path="profile" element={<ProfilePage />} /><Route path="platform" element={<PlatformAdminPage />} /></Route></Route><Route path="*" element={<Navigate to="/" replace />} /></Routes></AuthProvider></ConfirmProvider></BrowserRouter></QueryClientProvider>
}
