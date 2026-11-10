import { ArrowLeft } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Brand } from '../components/Brand'

type LegalSection = { title: string; paragraphs: string[] }

function LegalPage({ title, summary, sections }: { title: string; summary: string; sections: LegalSection[] }) {
  return <div className="legal-page">
    <header><Link to="/"><Brand /></Link><Link className="legal-back" to="/"><ArrowLeft size={16} /> Back to SukoonDesk</Link></header>
    <main>
      <span className="kicker">SukoonDesk legal</span>
      <h1>{title}</h1>
      <p className="legal-summary">{summary}</p>
      <p className="legal-date">Effective date: 8 November 2026</p>
      {sections.map(section => <section key={section.title}><h2>{section.title}</h2>{section.paragraphs.map(paragraph => <p key={paragraph}>{paragraph}</p>)}</section>)}
    </main>
    <footer><span>© 2026 SukoonDesk</span><span>Developed by Codeloom</span><nav><Link to="/terms">Terms</Link><Link to="/privacy">Privacy</Link></nav></footer>
  </div>
}

export function TermsPage() {
  return <LegalPage title="Terms of Service" summary="These terms explain the basic rules for using SukoonDesk as a society administrator, staff member, or resident." sections={[
    { title: '1. Using SukoonDesk', paragraphs: ['SukoonDesk provides society complaint registration, assignment, communication, reporting, and administration tools. You must provide accurate information and use the service only for lawful society operations.'] },
    { title: '2. Accounts and access', paragraphs: ['Keep your credentials confidential and notify your society administrator if you suspect unauthorized access. Society administrators control member approval, roles, permissions, and access within their society. Residents receive access only to their own complaint experience.'] },
    { title: '3. Complaint content', paragraphs: ['Do not submit unlawful, abusive, misleading, or unrelated material. You must have the right to upload any image or document you provide. Authorized society personnel may review complaint content to manage and resolve the concern.'] },
    { title: '4. Availability and changes', paragraphs: ['We aim to keep the service reliable, but uninterrupted availability is not guaranteed. Features may change to improve security, reliability, or usability. Important changes to these terms will be presented through the service or other reasonable notice.'] },
    { title: '5. Suspension and termination', paragraphs: ['Access may be limited or suspended for security risks, misuse, non-payment on a future paid plan, or violation of these terms. Society administrators may suspend users within their own society according to their policies.'] },
    { title: '6. Responsibility and liability', paragraphs: ['Societies remain responsible for their operational decisions, complaint handling, and legal obligations. To the extent permitted by law, SukoonDesk is not responsible for indirect losses or decisions made by society personnel using information in the service.'] },
    { title: '7. Contact', paragraphs: ['Questions about these terms should be sent to your society administrator. Platform-related questions can be sent to the SukoonDesk support address shown in official service emails.'] },
  ]} />
}

export function PrivacyPage() {
  return <LegalPage title="Privacy Policy" summary="This policy describes the basic information SukoonDesk processes and how it is used to operate society complaint services." sections={[
    { title: '1. Information we collect', paragraphs: ['We may process names, email addresses, phone numbers, CNIC numbers, property details, account credentials, roles, complaint descriptions, comments, uploaded images, status history, and technical security logs.'] },
    { title: '2. How information is used', paragraphs: ['Information is used to create and secure accounts, identify the correct society, register and resolve complaints, assign staff, communicate updates, produce reports, prevent abuse, and maintain the service.'] },
    { title: '3. Who can access information', paragraphs: ['Authorized staff can access information for their society according to assigned permissions. Residents can access their own member account and complaints. Platform administrators and service providers may process limited information when necessary to host, secure, store, or deliver the service.'] },
    { title: '4. Storage and retention', paragraphs: ['Records are retained while needed for society operations, audit history, security, and applicable legal requirements. A society may preserve complaint history after user access is removed. Retention periods may vary according to society policy and law.'] },
    { title: '5. Security', paragraphs: ['We use access controls, tenant separation, encrypted connections, password hashing, and restricted file access. No internet service can guarantee absolute security, so users must also protect their credentials and devices.'] },
    { title: '6. Your choices and rights', paragraphs: ['You may update available profile details and request correction or deletion through your society administrator. Some information may need to be retained for complaint history, fraud prevention, legal compliance, or legitimate operational records.'] },
    { title: '7. Cookies and service data', paragraphs: ['Essential cookies are used to maintain secure sign-in sessions and protect requests. We do not use these essential cookies for advertising.'] },
    { title: '8. Contact and updates', paragraphs: ['Privacy questions should first be directed to your society administrator. We may update this policy as the service evolves and will publish the revised effective date on this page.'] },
  ]} />
}
