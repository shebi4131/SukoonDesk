import { ShieldCheck } from 'lucide-react'
import { Link } from 'react-router-dom'

export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <Link className="brand" to="/" aria-label="SukoonDesk home">
      <span className="brand-mark"><ShieldCheck size={compact ? 20 : 24} strokeWidth={2.2} /></span>
      {!compact && <span>Sukoon<span>Desk</span></span>}
    </Link>
  )
}
