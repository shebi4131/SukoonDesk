import { useState, type FormEvent } from 'react'
import { Building2, ChevronRight, Search } from 'lucide-react'
import { api, ApiError } from '../lib/api'

export type SocietySearchResult = { name: string; slug: string }

export function SocietyFinder({ onSelect, compact = false }: { onSelect: (society: SocietySearchResult) => void; compact?: boolean }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SocietySearchResult[]>([])
  const [busy, setBusy] = useState(false)
  const [searched, setSearched] = useState(false)
  const [error, setError] = useState('')

  const search = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const term = query.trim()
    if (term.length < 2) {
      setError('Enter at least 2 letters from the society name.')
      return
    }
    setBusy(true); setError(''); setSearched(false)
    try {
      const matches = await api<SocietySearchResult[]>(`/api/public/societies?query=${encodeURIComponent(term)}`)
      setResults(matches); setSearched(true)
    } catch (requestError) {
      setResults([])
      setError(requestError instanceof ApiError ? requestError.message : 'Societies could not be searched. Please try again.')
    } finally { setBusy(false) }
  }

  return <div className={`society-finder${compact ? ' compact' : ''}`}>
    <form onSubmit={search} className="society-search-form">
      <label htmlFor="society-search">Society name</label>
      <div><Search size={18} /><input id="society-search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Type your society name" autoComplete="organization" /><button className="button button-primary" disabled={busy}>{busy ? 'Searching…' : 'Search'}</button></div>
    </form>
    {error && <div className="form-alert">{error}</div>}
    {searched && results.length === 0 && <div className="society-search-empty"><Building2 /><strong>No matching society found</strong><span>Check the spelling or ask your management office whether the society is active.</span></div>}
    {results.length > 0 && <div className="society-results" aria-live="polite"><p>Select your society to continue</p>{results.map(society => <button key={society.slug} type="button" onClick={() => onSelect(society)}><span><Building2 /></span><div><strong>{society.name}</strong><small>{society.slug}</small></div><ChevronRight /></button>)}</div>}
  </div>
}
