import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './public.css'
import './pricing.css'
import './enhancements.css'
import './i18n/i18n.css'
import App from './App.tsx'
import { I18nProvider } from './i18n/I18nProvider'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider><App /></I18nProvider>
  </StrictMode>,
)
