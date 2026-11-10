const labels: Record<string, string> = { JGI: 'JG-I', JGII: 'JG-II', KTExt: 'KT Ext' }
export const humanize = (value: string) => labels[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2')
const locale = () => document.documentElement.lang === 'ur-PK' ? 'ur-PK' : 'en-PK'
export const shortDate = (value: string) => new Intl.DateTimeFormat(locale(), { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(value))
export const relativeTime = (value: string) => {
  const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000)
  const formatter = new Intl.RelativeTimeFormat(locale(), { numeric: 'auto' })
  if (Math.abs(seconds) < 3600) return formatter.format(Math.round(seconds / 60), 'minute')
  if (Math.abs(seconds) < 86400) return formatter.format(Math.round(seconds / 3600), 'hour')
  return formatter.format(Math.round(seconds / 86400), 'day')
}
