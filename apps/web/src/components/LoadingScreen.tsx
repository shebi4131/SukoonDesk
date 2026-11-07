import { Brand } from './Brand'

export function LoadingScreen() {
  return (
    <div className="loading-screen" role="status" aria-label="Loading SukoonDesk">
      <Brand />
      <div className="loading-line"><span /></div>
      <p>Preparing your workspace…</p>
    </div>
  )
}
