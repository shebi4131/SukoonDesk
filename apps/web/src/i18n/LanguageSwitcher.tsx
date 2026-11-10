import { useState } from 'react'
import { Check, ChevronUp, Languages } from 'lucide-react'
import { supportedLanguages, useI18n } from './I18nProvider'

export function LanguageSwitcher() {
  const { language, setLanguage, t } = useI18n()
  const [open, setOpen] = useState(false)
  const selected = supportedLanguages.find(item => item.code === language)!
  return <div className="language-switcher">
    {open && <div className="language-menu" role="menu" aria-label={t('Choose language')}>
      <strong><Languages size={16} /> {t('Language')}</strong>
      {supportedLanguages.map(item => <button key={item.code} type="button" className={item.code === language ? 'selected' : ''} onClick={() => setLanguage(item.code)} role="menuitem">
        <span className="language-flag" aria-hidden="true">{item.flag}</span><span><b>{item.nativeLabel}</b><small>{item.label}</small></span>{item.code === language && <Check size={16} />}
      </button>)}
    </div>}
    <button type="button" className="language-trigger" onClick={() => setOpen(value => !value)} aria-expanded={open} aria-label={t('Choose language')}>
      <span aria-hidden="true">{selected.flag}</span><b>{selected.nativeLabel}</b><ChevronUp size={15} className={open ? '' : 'closed'} />
    </button>
  </div>
}
