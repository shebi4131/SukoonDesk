export type CurrentUser = {
  id: string
  membershipId: string
  displayName: string
  email: string
  mustChangePassword: boolean
  isPlatformAdmin: boolean
  tenant: { tenantId: string; name: string; slug: string }
  roles: string[]
  permissions: string[]
}

export type Permission = {
  id: string
  key: string
  name: string
  group: string
  description: string
}

export type Role = {
  id: string
  name: string
  description: string
  isSystem: boolean
  isActive: boolean
  canReceiveAssignments: boolean
  memberCount: number
  permissionKeys: string[]
}

export type Setting = {
  key: string
  value: string
  description: string
  isPublic: boolean
  updatedAt: string
}

export type Dashboard = {
  society: string
  greeting: string
  period: { from: string; to: string }
  filters: { categories: Array<{ id: string; name: string }>; statuses: string[]; priorities: string[]; phases: string[] }
  metrics: {
    totalComplaints: number
    openComplaints: number
    unassigned: number
    overdue: number
    dueToday: number
    resolvedThisMonth: number
    closed: number
    averageResolutionHours: number
    slaCompliance: number
    satisfaction: number
    activeMembers: number
    publicMembers: number
    activeStaff: number
    roleCount: number
  }
  charts: {
    byStatus: Array<{ name: string; value: number }>
    byCategory: Array<{ name: string; value: number }>
    byPriority: Array<{ name: string; value: number }>
    byPhase: Array<{ name: string; value: number }>
    byAssignee: Array<{ name: string; value: number }>
    trend: Array<{ label: string; created: number; resolved: number }>
    backlog: Array<{ name: string; value: number }>
  }
  recent: Array<{ id: string; number: string; title: string; status: string; priority: string; dueAt: string; createdAt: string; category: string; assignee: string | null; resident: string; source: string }>
}

export type ComplaintCategory = {
  id: string
  name: string
  description: string
  color: string
  defaultSlaHours: number
  defaultPriority: string
  isActive: boolean
  complaintCount: number
}

export type ComplaintSummary = {
  id: string
  number: string
  title: string
  location: string
  priority: string
  status: string
  dueAt: string
  createdAt: string
  updatedAt: string
  category: { id: string; name: string; color: string }
  resident: string
  assignedTo: string | null
  isOverdue: boolean
}

export type ComplaintDetail = {
  id: string
  number: string
  title: string
  description: string
  location: string
  priority: string
  status: string
  dueAt: string
  createdAt: string
  updatedAt: string
  category: { id: string; name: string; color: string; defaultSlaHours: number }
  resolvedAt: string | null
  closedAt: string | null
  createdByMembershipId: string
  assignedToMembershipId: string | null
  resident: { id: string; displayName: string; email: string; unitLabel: string }
  assignedTo: { id: string; displayName: string; email: string } | null
  attachments: Attachment[]
  comments: Array<{ id: string; body: string; isInternal: boolean; createdAt: string; author: string; authorMembershipId: string; attachments: Attachment[] }>
  history: Array<{ id: string; fromStatus: string | null; toStatus: string; note: string; createdAt: string; actor: string }>
  feedback: { rating: number; comment: string; createdAt: string } | null
}

export type Attachment = { id: string; fileName: string; contentType: string; size: number; url: string }

export type PublicMember = {
  id: string
  fullName: string
  whatsAppNumber: string
  email: string
  plotNo: string
  streetNo: string
  block: string
  projectId: string | null
  project: { id: string; name: string; code: string } | null
  notifyByEmail: boolean
  notifyByWhatsApp: boolean
  tenant: { id: string; name: string; slug: string }
}

export type PublicSociety = {
  id: string
  name: string
  slug: string
  projects: Array<{ id: string; name: string; code: string; description: string }>
  categories: Array<{ id: string; name: string; description: string; color: string; defaultSlaHours: number }>
}

export type PublicComplaint = {
  number: string
  title: string
  description: string
  location: string
  priority: string
  status: string
  dueAt: string
  createdAt: string
  updatedAt: string
  category: string
  attachments: Attachment[]
  comments: Array<{ id: string; body: string; createdAt: string; author: string; attachments: Attachment[] }>
  history: Array<{ toStatus: string; note: string; createdAt: string }>
}

export type Member = {
  id: string
  userId: string
  displayName: string
  email: string
  cnic: string | null
  phoneNumber: string | null
  unitLabel: string
  status: string
  joinedAt: string
  kind: 'Resident' | 'Team'
  roles: Array<{ id: string; name: string }>
}

export type NotificationFeed = {
  unreadCount: number
  items: Array<{ id: string; title: string; message: string; type: string; link: string; isRead: boolean; createdAt: string }>
}

export type ReportOverview = {
  society: string
  from: string
  to: string
  totals: { total: number; open: number; overdue: number; resolved: number; slaCompliance: number; averageResolutionHours: number; satisfaction: number }
  categories: Array<{ id: string; name: string }>
  statuses: string[]
  priorities: string[]
  phases: string[]
  byStatus: Array<{ name: string; value: number }>
  byCategory: Array<{ id?: string; name: string; value: number }>
  byPriority: Array<{ name: string; value: number }>
  byPhase: Array<{ name: string; value: number }>
  byAssignee: Array<{ name: string; value: number }>
  backlog: Array<{ name: string; value: number }>
  trend: Array<{ label: string; created: number; resolved: number }>
  recent: Array<{ number: string; title: string; category: string; status: string; priority: string; resident: string; assignee: string; createdAt: string; dueAt: string }>
}
