import { useState } from 'react'
import { ArrowLeft, Check, Mail, MessageCircle, ShieldCheck, Sparkles } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Brand } from '../components/Brand'

type Billing = 'monthly' | 'annual' | 'lifetime'
const monthly = [
  { name: 'Starter', price: 4999, note: 'For small communities starting organized complaint handling.', features: ['Up to 250 residents', 'Up to 10 staff accounts', '500 complaints / month', 'Resident complaint desk', 'Projects, RBAC and dashboards', 'Email notifications'] },
  { name: 'Professional', price: 9999, note: 'Best value for established housing societies.', popular: true, features: ['Up to 2,000 residents', 'Up to 50 staff accounts', '5,000 complaints / month', 'WhatsApp-ready notifications', 'Advanced reports and PDF export', 'Assignment rules and priority support'] },
  { name: 'Business', price: 17999, note: 'For large, multi-project societies and management groups.', features: ['Up to 10,000 residents', 'Up to 200 staff accounts', '25,000 complaints / month', 'Multiple projects and teams', 'Higher storage allowance', 'Premium onboarding and support'] },
  { name: 'Enterprise', price: 30000, note: 'Custom scale, controls, integrations and service levels.', features: ['Custom residents and staff limits', 'Custom complaint allowance', 'Dedicated deployment options', 'Custom integrations and branding', 'Migration assistance', 'Priority SLA and account manager'] },
]
const lifetime = [
  { name: 'Standard License', price: 250000, note: 'A self-hosted starting point for one society.', features: ['Single society license', 'Core complaint workflows', 'Projects, resident desk and RBAC', 'Installation and basic training'] },
  { name: 'Professional License', price: 450000, note: 'Complete operational package with advanced reporting.', popular: true, features: ['Single society license', 'All Professional features', 'Deployment and configuration', 'Admin and staff training', '90-day launch support'] },
  { name: 'Enterprise License', price: 750000, note: 'Custom deployment and integration engagement.', features: ['Custom scope and user limits', 'Branding and integrations', 'Data migration assistance', 'Technical handover', 'Negotiated support agreement'] },
]

export function PricingPage() {
  const [billing, setBilling] = useState<Billing>('monthly')
  const plans = billing === 'lifetime' ? lifetime : monthly
  const quote = (plan: string) => encodeURIComponent(`Hello SukoonDesk, I am interested in the ${plan} ${billing === 'lifetime' ? 'one-time license' : billing + ' subscription'}. Please send me a quotation.`)
  return <div className="pricing-page">
    <header><Link to="/"><Brand /></Link><nav><Link to="/" className="pricing-back"><ArrowLeft size={16} /> Home</Link><a className="button button-primary" href="mailto:shoaibahmed4131@gmail.com?subject=SukoonDesk%20demo%20request">Request a demo</a></nav></header>
    <main>
      <section className="pricing-hero"><span><Sparkles size={15} /> Simple pricing for Pakistan</span><h1>Turn every concern into<br />accountable action.</h1><p>Choose a flexible subscription or own a self-hosted license—without hidden platform fees.</p><div className="pricing-toggle"><button className={billing === 'monthly' ? 'active' : ''} onClick={() => setBilling('monthly')}>Monthly</button><button className={billing === 'annual' ? 'active' : ''} onClick={() => setBilling('annual')}>Annual <em>Best value</em></button><button className={billing === 'lifetime' ? 'active' : ''} onClick={() => setBilling('lifetime')}>One-time</button></div></section>
      <section className={`pricing-grid ${billing === 'lifetime' ? 'three' : ''}`}>{plans.map(plan => {
        const amount = billing === 'annual' ? plan.price * 10 : plan.price
        return <article className={plan.popular ? 'popular' : ''} key={plan.name}>{plan.popular && <b className="popular-label">Most popular</b>}<div className="plan-top"><h2>{plan.name}</h2><p>{plan.note}</p></div><div className="plan-price"><small>PKR</small><strong>{amount.toLocaleString('en-PK')}</strong><span>/ {billing === 'annual' ? 'year' : billing === 'monthly' ? 'month' : 'one-time'}</span></div>{billing === 'annual' && <p className="plan-saving">Save PKR {(plan.price * 2).toLocaleString('en-PK')} every year</p>}<a className="button button-primary button-block" href={`mailto:shoaibahmed4131@gmail.com?subject=${encodeURIComponent(`SukoonDesk ${plan.name} quotation`)}&body=${quote(plan.name)}`}>Request quotation</a><ul>{plan.features.map(item => <li key={item}><Check size={15} /> {item}</li>)}</ul></article>
      })}</section>
      <section className="pricing-notes"><article><ShieldCheck /><div><h3>Setup & onboarding</h3><p>Remote setup and basic onboarding can be included. On-site training, custom migration and integrations are quoted separately.</p></div></article><article><Sparkles /><div><h3>Ownership support</h3><p>One-time licenses exclude hosting and include the agreed launch period. Optional annual maintenance is typically 18–22% of license value.</p></div></article></section>
      <section className="pricing-contact"><div><span>Need a tailored plan?</span><h2>Let’s price SukoonDesk for your society.</h2><p>Tell us your resident count, staff size, projects and preferred deployment. We’ll send a clear quotation.</p></div><div><a className="button button-light" href="mailto:shoaibahmed4131@gmail.com?subject=SukoonDesk%20quotation"><Mail /> Email quotation request</a><a className="button whatsapp-button" target="_blank" rel="noreferrer" href={`https://wa.me/923066330833?text=${quote('custom')}`}><MessageCircle /> WhatsApp us</a></div></section>
    </main>
    <footer><Brand /><span>Prices exclude applicable taxes and third-party usage charges.</span><div><Link to="/terms">Terms</Link><Link to="/privacy">Privacy</Link><span>Developed by Codeloom</span></div></footer>
  </div>
}
