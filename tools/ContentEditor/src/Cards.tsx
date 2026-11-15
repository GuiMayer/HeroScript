import type { Copy } from './i18n'

export interface CardsProps {
  apiKey: string
  setting: string
  writable: boolean
  locale: 'en' | 'pt-BR'
  copy: Copy
}

export default function Cards({ copy }: CardsProps) {
  return <section className="empty"><h1>{copy.cards}</h1><p>{copy.loading}</p></section>
}
