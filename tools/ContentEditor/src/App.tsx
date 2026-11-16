import { lazy, Suspense, useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { BookOpen, Boxes, ChevronDown, CircleHelp, Compass, Layers3, Menu, Network, Settings2, Sparkles, Swords, Tags, Users } from 'lucide-react'
import { api, ApiError, type Schemas } from './api/client'
import { cardCopy } from './card-copy'
import { Workspace } from './editor-state'
import { dictionary, preference, type Locale } from './i18n'
import { Modal } from './Modal'

const Cards = lazy(() => import('./Cards'))
const RulesEditor = lazy(() => import('./RulesEditor'))
const areas = [
  ['cards', Layers3], ['rules', Network], ['effects', Sparkles], ['resources', Boxes],
  ['entities', Users], ['decks', Tags], ['runs', Swords], ['settings', Settings2],
] as const
type Area = (typeof areas)[number][0]
type Theme = 'system' | 'light' | 'dark'

function locationState() {
  const url = new URL(window.location.href)
  const area = url.searchParams.get('area')
  return {
    area: (areas.find(([id]) => id === area)?.[0] ?? 'cards') as Area,
    setting: url.searchParams.get('setting') ?? localStorage.getItem('heroscript.editor.setting') ?? '',
  }
}

export function Skeleton({ label }: { label: string }) {
  return <div className="skeleton-layout" aria-busy="true" aria-label={label} role="status">
    <div className="skeleton side" /><div className="skeleton main" />
  </div>
}

export function App() {
  const [locale, setLocale] = useState<Locale>(() => preference('locale', ['pt-BR', 'en'],
    navigator.language.startsWith('pt') ? 'pt-BR' : 'en'))
  const [theme, setTheme] = useState<Theme>(() => preference('theme', ['system', 'light', 'dark'], 'system'))
  const [location, setLocation] = useState(locationState)
  const [apiKey, setApiKey] = useState('')
  const [launching, setLaunching] = useState(() => new URLSearchParams(window.location.hash.slice(1)).has('launch'))
  const [launchFailed, setLaunchFailed] = useState(false)
  const [input, setInput] = useState('')
  const [session, setSession] = useState(0)
  const [drafts, setDrafts] = useState(() => new Workspace())
  const [menu, setMenu] = useState(false)
  const [preferences, setPreferences] = useState(false)
  const queryClient = useQueryClient()
  const copy = dictionary[locale]
  const connection = useQuery({
    queryKey: ['connection', session], enabled: !!apiKey,
    queryFn: async ({ signal }) => {
      const [settings, caps] = await Promise.all([
        api<Schemas['CardAuthoringSettings']>(apiKey, '/settings', { signal }),
        api<Schemas['AuthoringCapabilities']>(apiKey, '/capabilities', { signal }),
      ])
      return { ...settings.data, capabilities: caps.data }
    },
  })
  const settings = connection.data?.items?.filter((item) => item.settingId) ?? []
  const selected = settings.find((item) => item.settingId === location.setting) ??
    settings.find((item) => item.writablePackageId) ?? settings[0]
  const settingId = selected?.settingId ?? ''
  const connected = connection.isSuccess

  useEffect(() => {
    const url = new URL(window.location.href)
    const code = new URLSearchParams(url.hash.slice(1)).get('launch')
    if (code === null) return
    url.hash = ''
    window.history.replaceState(window.history.state, '', url)

    async function exchange() {
      try {
        const response = await fetch('/editor/session', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code }),
        })
        if (!response.ok) throw new Error()
        const { key } = await response.json()
        if (typeof key !== 'string' || !key) throw new Error()
        setApiKey(key)
      } catch {
        setLaunchFailed(true)
      } finally {
        setLaunching(false)
      }
    }
    void exchange()
  }, [])

  useEffect(() => {
    document.documentElement.lang = locale
    localStorage.setItem('heroscript.editor.locale', locale)
  }, [locale])
  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem('heroscript.editor.theme', theme)
  }, [theme])
  useEffect(() => {
    const onPop = () => setLocation(locationState())
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (!drafts.hasUnconfirmed()) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', beforeUnload)
    return () => window.removeEventListener('beforeunload', beforeUnload)
  }, [drafts])
  useEffect(() => {
    if (!settingId || location.setting === settingId) return
    const url = new URL(window.location.href)
    url.searchParams.set('setting', settingId)
    window.history.replaceState(null, '', url)
    localStorage.setItem('heroscript.editor.setting', settingId)
    setLocation((current) => ({ ...current, setting: settingId }))
  }, [settingId, location.setting])

  function navigate(area: Area, setting = settingId) {
    const url = new URL(window.location.href)
    url.searchParams.set('area', area)
    if (setting) {
      url.searchParams.set('setting', setting)
      localStorage.setItem('heroscript.editor.setting', setting)
    }
    window.history.pushState(null, '', url)
    setLocation({ area, setting })
    setMenu(false)
  }

  function connect(value: string) {
    if (drafts.hasUnconfirmed() && !window.confirm(cardCopy[locale].warnDisconnect)) return
    queryClient.clear()
    setDrafts(new Workspace())
    setApiKey(value)
    setLaunchFailed(false)
    setInput('')
    setSession((current) => current + 1)
  }

  return <div className="app-shell">
    <aside className={`sidebar ${menu ? 'is-open' : ''}`}>
      <a className="brand" href="/editor/" onClick={(event) => { event.preventDefault(); navigate('cards') }}>
        <span className="brand-icon"><BookOpen size={21} /></span>
        <span><strong>HeroScript</strong><small>{copy.editor}</small></span>
      </a>
      <div className="nav-caption">{copy.workspace}</div>
      <nav aria-label={copy.workspace}>
        {areas.map(([id, Icon]) => <button key={id} className={`nav-link ${location.area === id ? 'selected' : ''}`}
          aria-current={location.area === id ? 'page' : undefined} onClick={() => navigate(id)}>
          <Icon size={17} /><span>{copy[id]}</span>
        </button>)}
      </nav>
      <div className="sidebar-bottom">
        <button className="nav-link" onClick={() => setPreferences(true)}><Settings2 size={17} />{copy.preferences}</button>
        <a href="/docs/api" className="nav-link" target="_blank" rel="noreferrer"><CircleHelp size={17} />API</a>
      </div>
    </aside>
    {menu && <button className="nav-overlay" aria-label={copy.close} onClick={() => setMenu(false)} />}
    <section className="workspace">
      <header className="topbar">
        <button className="icon-button mobile-menu" aria-label={copy.openNavigation} onClick={() => setMenu(true)}><Menu size={20} /></button>
        <div className="setting-select"><Compass size={17} /><label htmlFor="setting">{copy.gameSetting}</label>
          <select id="setting" value={settingId} onChange={(event) => navigate(location.area, event.target.value)} disabled={!settings.length}>
            {!settings.length && <option value="">{copy.chooseSetting}</option>}
            {settings.map((item) => <option key={item.settingId} value={item.settingId ?? ''}>{item.displayName || item.settingId}</option>)}
          </select><ChevronDown size={14} />
        </div>
        <div className="top-actions">
          {connected && <button className="connection" onClick={() => connect('')} title={copy.disconnect}>
            <span className="dot" />{copy.connected}<span className="disconnect-label">{copy.disconnect}</span>
          </button>}
          <button className="icon-button" title={copy.preferences} aria-label={copy.preferences} onClick={() => setPreferences(true)}><Settings2 size={18} /></button>
        </div>
      </header>
      <main className="workspace-main">
        {launching ? <Skeleton label={copy.connecting} /> : !apiKey || connection.isError ? <section className="connection-card">
          <div className="empty-icon"><Network size={26} /></div>
          <h1>{copy.connect}</h1><p>{copy.connectHint}</p>
          <form onSubmit={(event) => { event.preventDefault(); if (input.trim()) connect(input.trim()) }}>
            <label htmlFor="admin-key">{copy.key}</label>
            <input id="admin-key" type="password" autoComplete="off" value={input} onChange={(event) => setInput(event.target.value)} required />
            {connection.isError && <p className="error" role="alert">{connection.error instanceof ApiError && connection.error.status === 401 ? copy.invalidKey : copy.unavailable}</p>}
            {launchFailed && <p className="error" role="alert">{copy.launchFailed}</p>}
            <button className="primary" type="submit" disabled={!input.trim()}>{copy.connectButton}</button>
          </form>
        </section> : !connected ? <Skeleton label={copy.connecting} />
          : !settingId ? <section className="empty"><h1>{copy.noSettings}</h1>
            <button className="secondary" onClick={() => void connection.refetch()}>{copy.retry}</button></section>
          : <Suspense fallback={<Skeleton label={copy.loading} />}>
            {location.area === 'cards' ? <Cards apiKey={apiKey} setting={settingId} locale={locale} copy={copy} workspace={drafts}
              writable={!!selected?.writablePackageId && !!connection.data?.capabilities.canApply} />
              : location.area === 'rules' ? <RulesEditor apiKey={apiKey} setting={settingId} locale={locale} copy={copy} workspace={drafts}
                writable={!!selected?.writablePackageId && !!connection.data?.capabilities.canApply} />
              : <section className="empty"><div className="empty-icon"><Layers3 size={26} /></div>
                <h1>{copy[location.area]}</h1><p>{copy.pendingPage}</p>
                <button className="secondary" onClick={() => navigate('cards')}>{copy.backToCards}</button>
              </section>}
          </Suspense>}
        {!!connection.data?.diagnostics?.length && <details className="catalog-diagnostics">
          <summary>{copy.diagnostics}</summary><ul>{connection.data.diagnostics.map((item, index) => <li key={index}>{item.message}</li>)}</ul>
        </details>}
      </main>
    </section>
    {preferences && <Modal title={copy.preferences} closeLabel={copy.close} onClose={() => setPreferences(false)}>
        <label>{copy.language}<select value={locale} onChange={(event) => setLocale(event.target.value as Locale)}>
          <option value="pt-BR">Português (Brasil)</option><option value="en">English</option>
        </select></label>
        <label>{copy.theme}<select value={theme} onChange={(event) => setTheme(event.target.value as Theme)}>
          <option value="system">{copy.system}</option><option value="light">{copy.light}</option><option value="dark">{copy.dark}</option>
        </select></label>
    </Modal>}
  </div>
}
