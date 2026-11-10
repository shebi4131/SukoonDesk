import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { BarChart3, Bell, Building2, CheckCheck, ChevronDown, LayoutDashboard, LogOut, MapPinned, Menu, MessageSquareText, PanelLeftClose, PanelLeftOpen, Settings, Shield, Tags, UserRoundCog, Users, X } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/auth-context'
import { Brand } from './Brand'
import { api } from '../lib/api'
import type { NotificationFeed } from '../lib/types'
import { relativeTime } from '../lib/format'

const navigation = [
  { to: '/app', label: 'Overview', icon: LayoutDashboard, end: true, permission: 'dashboard.view' },
  { to: '/app/complaints', label: 'Complaints', icon: MessageSquareText, permission: 'complaints.view_own' },
  { to: '/app/people', label: 'Residents & team', icon: Users, permission: 'users.view' },
  { to: '/app/reports', label: 'Reports', icon: BarChart3, permission: 'reports.view' },
  { to: '/app/categories', label: 'Categories & routing', icon: Tags, permission: 'settings.view' },
  { to: '/app/projects', label: 'Projects & phases', icon: MapPinned, permission: 'settings.view' },
  { to: '/app/roles', label: 'Roles & access', icon: Shield, permission: 'roles.view' },
  { to: '/app/settings', label: 'Settings', icon: Settings, permission: 'settings.view' },
]

export function AppShell() {
  const { user, hasPermission, signOut } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const [profileOpen, setProfileOpen] = useState(false)
  const [notificationsOpen, setNotificationsOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(() => window.localStorage.getItem('sukoondesk.sidebarCollapsed') === 'true')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const notifications = useQuery({ queryKey: ['notifications'], queryFn: () => api<NotificationFeed>('/api/notifications/'), refetchInterval: 30_000 })
  const markRead = useMutation({ mutationFn: (id: string) => api<void>(`/api/notifications/${id}/read`, { method: 'POST' }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }) })
  const readAll = useMutation({ mutationFn: () => api<void>('/api/notifications/read-all', { method: 'POST' }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }) })

  const logout = async () => {
    await signOut()
    navigate('/')
  }
  const toggleCollapsed = () => setCollapsed(value => {
    window.localStorage.setItem('sukoondesk.sidebarCollapsed', String(!value))
    return !value
  })

  return (
    <div className={`app-frame ${collapsed ? 'sidebar-collapsed' : ''}`}>
      <aside className={`sidebar ${menuOpen ? 'is-open' : ''}`}>
        <div className="sidebar-top">
          {(!collapsed || menuOpen) && <Brand />}
          <button className="icon-button sidebar-close" onClick={() => setMenuOpen(false)} aria-label="Close menu"><X size={20} /></button>
        </div>
        <button className="sidebar-collapse" onClick={toggleCollapsed} aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'} title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}>{collapsed ? <PanelLeftOpen size={16} /> : <PanelLeftClose size={16} />}</button>
        <div className="society-switcher">
          <span className="society-avatar">{user?.tenant.name.charAt(0)}</span>
          <span><small>Current society</small><strong>{user?.tenant.name}</strong></span>
          <ChevronDown size={16} />
        </div>
        <nav className="side-nav" aria-label="Main navigation">
          <p>Workspace</p>
          {navigation.filter(item => item.permission === 'complaints.view_own'
            ? hasPermission('complaints.view_own') || hasPermission('complaints.view_all')
            : hasPermission(item.permission)).map(({ icon: Icon, ...item }) => (
            <NavLink key={item.to} to={item.to} end={item.end} onClick={() => setMenuOpen(false)}>
              <Icon size={19} /><span>{item.label}</span>
            </NavLink>
          ))}
          {user?.isPlatformAdmin && <><p>Platform</p><NavLink to="/app/platform" onClick={() => setMenuOpen(false)}><Building2 size={19} /><span>Tenant administration</span></NavLink></>}
        </nav>
        <button className="sidebar-signout" onClick={logout} title="Sign out"><LogOut size={18} /><span>Sign out</span></button>
      </aside>
      {menuOpen && <button className="sidebar-scrim" onClick={() => setMenuOpen(false)} aria-label="Close navigation" />}

      <main className="app-main">
        <header className="app-header">
          <button className="icon-button mobile-menu" onClick={() => setMenuOpen(true)} aria-label="Open menu"><Menu size={21} /></button>
          <div className="header-spacer" />
          <div className="notification-wrap"><button className="icon-button notification-button" aria-label="Notifications" onClick={() => setNotificationsOpen(x => !x)}><Bell size={20} />{!!notifications.data?.unreadCount && <span />}</button>{notificationsOpen && <div className="notification-menu"><header><div><strong>Notifications</strong><small>{notifications.data?.unreadCount ?? 0} unread</small></div><button onClick={() => readAll.mutate()}><CheckCheck size={15} /> Mark all read</button></header><div>{notifications.data?.items.map(item => <button className={item.isRead ? '' : 'unread'} key={item.id} onClick={() => { if (!item.isRead) markRead.mutate(item.id); setNotificationsOpen(false); if (item.link) navigate(item.link) }}><i className={`notification-type ${item.type}`} /><span><strong>{item.title}</strong><small>{item.message}</small><time>{relativeTime(item.createdAt)}</time></span></button>)}{!notifications.data?.items.length && <p>No notifications yet.</p>}</div></div>}</div>
          <div className="profile-wrap">
            <button className="profile-button" onClick={() => setProfileOpen(value => !value)}>
              <span className="profile-avatar">{user?.displayName.split(' ').map(x => x[0]).slice(0, 2).join('')}</span>
              <span><strong>{user?.displayName}</strong><small>{user?.roles[0]}</small></span>
              <ChevronDown size={16} />
            </button>
            {profileOpen && (
              <div className="profile-menu">
                <div><strong>{user?.displayName}</strong><span>{user?.email}</span></div>
                <button className="profile-settings" onClick={() => { setProfileOpen(false); navigate('/app/profile') }}><UserRoundCog size={17} /> Profile settings</button>
                <button onClick={logout}><LogOut size={17} /> Sign out</button>
              </div>
            )}
          </div>
        </header>
        <div className="page-container"><Outlet /></div>
      </main>
    </div>
  )
}
