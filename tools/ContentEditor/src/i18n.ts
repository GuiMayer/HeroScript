export type Locale = 'en' | 'pt-BR'
export const dictionary = {
  en: {
    editor: 'Content editor', workspace: 'Workspace', cards: 'Cards', rules: 'Rules and flows',
    effects: 'Effects and status', resources: 'Resources and formulas', entities: 'Entities',
    decks: 'Decks and pools', runs: 'Runs and progression', settings: 'Settings and packages',
    gameSetting: 'Game setting', chooseSetting: 'Select a setting', loading: 'Loading…',
    pendingPage: 'This area is not implemented yet.', backToCards: 'Open Cards',
    connect: 'Connect to the engine', key: 'Administrative key', connectButton: 'Connect',
    connectHint: 'Enter the administrative key configured for your local HeroScript engine.',
    connecting: 'Connecting…', connected: 'Connected', disconnect: 'Disconnect',
    unavailable: 'Could not connect to the engine.', invalidKey: 'The engine rejected this administrative key.',
    noSettings: 'No game settings are available.', retry: 'Try again', preferences: 'Preferences',
    language: 'Language', theme: 'Theme', system: 'System', light: 'Light', dark: 'Dark',
    diagnostics: 'Engine diagnostics', readOnly: 'Read only', engineDisabled: 'Check the engine and its administrative settings.',
    openNavigation: 'Open navigation', close: 'Close',
  },
  'pt-BR': {
    editor: 'Editor de conteúdo', workspace: 'Área de trabalho', cards: 'Cartas', rules: 'Regras e fluxos',
    effects: 'Efeitos e status', resources: 'Recursos e fórmulas', entities: 'Entidades',
    decks: 'Decks e pools', runs: 'Runs e progressão', settings: 'Settings e pacotes',
    gameSetting: 'Game setting', chooseSetting: 'Escolha um setting', loading: 'Carregando…',
    pendingPage: 'Esta área ainda não foi implementada.', backToCards: 'Abrir Cartas',
    connect: 'Conectar à engine', key: 'Chave administrativa', connectButton: 'Conectar',
    connectHint: 'Informe a chave administrativa configurada na sua engine HeroScript local.',
    connecting: 'Conectando…', connected: 'Conectado', disconnect: 'Desconectar',
    unavailable: 'Não foi possível conectar à engine.', invalidKey: 'A engine recusou a chave administrativa.',
    noSettings: 'Nenhum game setting está disponível.', retry: 'Tentar novamente', preferences: 'Preferências',
    language: 'Idioma', theme: 'Tema', system: 'Sistema', light: 'Claro', dark: 'Escuro',
    diagnostics: 'Diagnóstico da engine', readOnly: 'Somente leitura', engineDisabled: 'Verifique a engine e suas configurações administrativas.',
    openNavigation: 'Abrir navegação', close: 'Fechar',
  },
} as const

export type Copy = (typeof dictionary)[Locale]

export function preference<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  const value = localStorage.getItem(`heroscript.editor.${key}`)
  return allowed.includes(value as T) ? value as T : fallback
}
