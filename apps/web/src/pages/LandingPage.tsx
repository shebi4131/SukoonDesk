import { ArrowRight, BarChart3, BellRing, CheckCircle2, Route, ShieldCheck, UsersRound } from 'lucide-react'
import { Link, Navigate } from 'react-router-dom'
import { Brand } from '../components/Brand'
import { useAuth } from '../auth/auth-context'

export function LandingPage() {
  const { user } = useAuth()
  if (user) return <Navigate to="/app" replace />

  return (
    <div className="marketing-page">
      <header className="marketing-nav shell">
        <Brand />
        <nav><a href="#features">Features</a><a href="#workflow">How it works</a><a href="#security">Security</a></nav>
        <div className="nav-actions"><Link to="/public" className="button button-ghost">Register complaint</Link><Link to="/sign-in" className="text-link">Staff sign in</Link><Link to="/sign-up" className="button button-primary">Start free</Link></div>
      </header>

      <main>
        <section className="hero shell">
          <div className="hero-copy">
            <span className="eyebrow"><span /> Built for better communities</span>
            <h1>Every concern deserves a <em>clear resolution.</em></h1>
            <p>SukoonDesk brings residents, supervisors, and management together in one calm, accountable workspace.</p>
            <div className="hero-actions"><Link to="/public" className="button button-primary button-large">Register a complaint <ArrowRight size={18} /></Link><Link to="/sign-up" className="button button-ghost button-large">Create your society</Link></div>
            <div className="hero-proof"><span><CheckCircle2 size={17} /> Free to start</span><span><CheckCircle2 size={17} /> Set up in minutes</span><span><CheckCircle2 size={17} /> No card required</span></div>
          </div>
          <div className="hero-visual" aria-label="SukoonDesk dashboard preview">
            <div className="preview-glow" />
            <div className="preview-window">
              <div className="preview-bar"><i /><i /><i /><span>sukoondesk.app/green-view</span></div>
              <div className="preview-body">
                <div className="preview-side"><Brand compact /><span className="active" /><span /><span /><span /></div>
                <div className="preview-content">
                  <div className="preview-heading"><div><small>Good morning, Ayesha</small><strong>Community overview</strong></div><button>+ New complaint</button></div>
                  <div className="preview-cards"><article><small>Open concerns</small><b>24</b><em>↓ 12% this week</em></article><article><small>Due today</small><b>06</b><em className="warn">Needs attention</em></article><article><small>Resolved</small><b>128</b><em>↑ 18% this month</em></article></div>
                  <div className="preview-list"><div className="preview-list-head"><strong>Recent concerns</strong><span>View all</span></div>{['Water pressure · Block C','Street light · Gate 2','Lift maintenance · Tower A'].map((item, index) => <div className="preview-row" key={item}><i className={`status-dot dot-${index}`} /><span>{item}<small>Updated {index + 1}h ago</small></span><em>{['In progress','Assigned','Resolved'][index]}</em></div>)}</div>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section id="features" className="feature-section shell">
          <div className="section-heading"><span className="kicker">One shared source of truth</span><h2>Calm operations. Happier residents.</h2><p>Everything your team needs to receive, route, resolve, and learn from community concerns.</p></div>
          <div className="feature-grid">
            <article><span className="feature-icon"><Route /></span><h3>Smart assignment</h3><p>Route concerns automatically by category, zone, shift, and workload—with manual control when needed.</p></article>
            <article><span className="feature-icon purple"><BellRing /></span><h3>Updates that reach people</h3><p>Keep everyone informed with in-app, email, and WhatsApp-ready notifications.</p></article>
            <article><span className="feature-icon amber"><BarChart3 /></span><h3>Reports that guide action</h3><p>See response times, SLA health, recurring issues, and resident satisfaction at a glance.</p></article>
            <article id="security"><span className="feature-icon green"><ShieldCheck /></span><h3>Access you control</h3><p>Tenant-aware roles and fine-grained permissions protect every society and every action.</p></article>
          </div>
        </section>

        <section id="workflow" className="workflow-section"><div className="shell workflow-inner"><div><span className="kicker light">From concern to closure</span><h2>A simple path everyone can follow.</h2><p>Residents always know what is happening. Teams always know what to do next.</p></div><div className="workflow-steps">{[['01','Resident submits','Details, location and evidence arrive together.'],['02','SukoonDesk routes','The right supervisor is assigned with an SLA.'],['03','Everyone stays updated','A clear timeline keeps residents and teams aligned.'],['04','Resolve and improve','Close the loop, collect feedback and spot trends.']].map(([number,title,text]) => <article key={number}><span>{number}</span><div><h3>{title}</h3><p>{text}</p></div></article>)}</div></div></section>

        <section className="cta-section shell"><div><span><UsersRound /></span><h2>Bring more sukoon to your society.</h2><p>Start with the foundation today and grow into complete community operations.</p><Link to="/sign-up" className="button button-light button-large">Create your workspace <ArrowRight size={18} /></Link></div></section>
      </main>
      <footer className="marketing-footer shell"><Brand /><p>© 2026 SukoonDesk. Every concern, resolved. · Developed by Codeloom</p><div><Link to="/terms">Terms</Link><Link to="/privacy">Privacy</Link><a href="#security">Security</a></div></footer>
    </div>
  )
}
