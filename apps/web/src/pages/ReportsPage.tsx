import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { BarChart3, CheckCircle2, Clock3, Download, Filter, Gauge, Star, TriangleAlert } from 'lucide-react'
import { api } from '../lib/api'
import type { ReportOverview } from '../lib/types'
import { humanize, shortDate } from '../lib/format'

const today = new Date().toISOString().slice(0, 10)
const monthAgo = new Date(Date.now() - 30 * 86400000).toISOString().slice(0, 10)

export function ReportsPage() {
  const [filters, setFilters] = useState({ from: monthAgo, to: today, status: '', categoryId: '', priority: '', phase: '' })
  const queryString = useMemo(() => { const query = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => value && query.set(key, value)); return query.toString() }, [filters])
  const report = useQuery({ queryKey: ['reports', filters], queryFn: () => api<ReportOverview>(`/api/reports/overview?${queryString}`) })
  const data = report.data
  const maximum = (rows: Array<{ value: number }> | undefined) => Math.max(...(rows?.map(x => x.value) ?? [1]), 1)
  const maxTrend = Math.max(...(data?.trend.flatMap(x => [x.created, x.resolved]) ?? [1]), 1)
  if (report.isLoading) return <div className="page-loading">Preparing performance report…</div>
  if (!data) return <div className="page-loading">Report data is unavailable.</div>
  return <>
    <div className="page-heading"><div><p className="breadcrumb">Insights / Reports</p><h1>Service performance</h1><p>Filter operational outcomes, discover patterns, and export a management-ready PDF.</p></div><a className="button button-primary" href={`/api/reports/export.pdf?${queryString}`} download><Download size={17} /> Export PDF</a></div>
    <section className="dashboard-filter panel"><div><Filter size={17} /><strong>Report filters</strong></div><label>From<input type="date" value={filters.from} onChange={e => setFilters(x => ({ ...x, from: e.target.value }))} /></label><label>To<input type="date" value={filters.to} onChange={e => setFilters(x => ({ ...x, to: e.target.value }))} /></label><label>Status<select value={filters.status} onChange={e => setFilters(x => ({ ...x, status: e.target.value }))}><option value="">All statuses</option>{data.statuses.map(x => <option key={x}>{humanize(x)}</option>)}</select></label><label>Category<select value={filters.categoryId} onChange={e => setFilters(x => ({ ...x, categoryId: e.target.value }))}><option value="">All categories</option>{data.categories.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Priority<select value={filters.priority} onChange={e => setFilters(x => ({ ...x, priority: e.target.value }))}><option value="">All priorities</option>{data.priorities.map(x => <option key={x}>{x}</option>)}</select></label><label>Phase<select value={filters.phase} onChange={e => setFilters(x => ({ ...x, phase: e.target.value }))}><option value="">All phases</option>{data.phases.map(x => <option key={x}>{humanize(x)}</option>)}</select></label></section>
    <div className="report-metrics advanced"><article><BarChart3 /><span><small>Total complaints</small><strong>{data.totals.total}</strong></span></article><article><TriangleAlert /><span><small>Overdue</small><strong>{data.totals.overdue}</strong></span></article><article><Gauge /><span><small>SLA compliance</small><strong>{data.totals.slaCompliance}%</strong></span></article><article><Clock3 /><span><small>Avg. resolution</small><strong>{data.totals.averageResolutionHours}h</strong></span></article><article><Star /><span><small>Satisfaction</small><strong>{data.totals.satisfaction}/5</strong></span></article><article><CheckCircle2 /><span><small>Resolved</small><strong>{data.totals.resolved}</strong></span></article></div>
    <div className="reports-grid reports-advanced">
      <section className="panel report-panel wide"><div className="panel-heading"><div><h2>12-week activity trend</h2><p>New concerns compared with completed resolutions.</p></div><div className="chart-legend"><span>Created</span><span className="resolved">Resolved</span></div></div><div className="trend-chart">{data.trend.map(point => <div className="trend-column" key={point.label}><div className="bar-pair"><i style={{ height: `${Math.max(point.created / maxTrend * 135, point.created ? 7 : 0)}px` }} title={`${point.created} created`} /><i className="resolved" style={{ height: `${Math.max(point.resolved / maxTrend * 135, point.resolved ? 7 : 0)}px` }} title={`${point.resolved} resolved`} /></div><span>{point.label}</span></div>)}</div></section>
      <Chart title="Status distribution" description="Current portfolio in the selected period." rows={data.byStatus} max={maximum(data.byStatus)} />
      <Chart title="Top categories" description="Services receiving the most attention." rows={data.byCategory} max={maximum(data.byCategory)} />
      <Chart title="Priority mix" description="Urgency profile for matching complaints." rows={data.byPriority} max={maximum(data.byPriority)} />
      <Chart title="Workload by assignee" description="Ownership across the operations team." rows={data.byAssignee} max={maximum(data.byAssignee)} />
      <section className="panel report-panel"><div className="panel-heading"><div><h2>Backlog age</h2><p>Age of concerns that remain open.</p></div></div><div className="backlog-grid">{data.backlog.map((item, index) => <article className={`age-${index}`} key={item.name}><strong>{item.value}</strong><span>{item.name}</span></article>)}</div></section>
      <Chart title="Complaints by phase" description="Registered-property hotspots." rows={data.byPhase} max={maximum(data.byPhase)} />
      <section className="panel report-panel wide report-table"><div className="panel-heading"><div><h2>Complaint register</h2><p>The latest 20 complaints matching these filters.</p></div></div><div className="report-row report-head"><span>Concern</span><span>Resident</span><span>Assignee</span><span>Status</span><span>Created</span></div>{data.recent.map(row => <div className="report-row" key={row.number}><span><strong>{row.title}</strong><small>{row.number} · {row.category} · {row.priority}</small></span><span>{row.resident}</span><span>{row.assignee}</span><span><i className={`status-chip status-${row.status.toLowerCase()}`}>{humanize(row.status)}</i></span><time>{shortDate(row.createdAt)}</time></div>)}{!data.recent.length && <div className="empty-state compact"><BarChart3 /><h3>No matching complaints</h3><p>Adjust the filters to broaden the report.</p></div>}</section>
    </div>
  </>
}

function Chart({ title, description, rows, max }: { title: string; description: string; rows: Array<{ name: string; value: number }>; max: number }) {
  return <section className="panel report-panel"><div className="panel-heading"><div><h2>{title}</h2><p>{description}</p></div></div><div className="dashboard-bars">{rows.slice(0, 7).map(item => <div key={item.name}><span>{humanize(item.name)}<strong>{item.value}</strong></span><b><i style={{ width: `${item.value / max * 100}%` }} /></b></div>)}{!rows.length && <p className="chart-empty">No matching data for this period.</p>}</div></section>
}
