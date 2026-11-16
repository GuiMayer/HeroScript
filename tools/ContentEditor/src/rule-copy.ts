export const ruleKinds = [
  'combat-rules', 'flow-rules', 'phase-sequences', 'modes',
  'capability-policies', 'content-binding-policies', 'replay-policies', 'timeline-policies',
] as const
export type RuleKind = (typeof ruleKinds)[number]

export const ruleCopy = {
  en: {
    intro: 'Configure combat rules, turn phases, game modes and policies.',
    rules: 'Rule catalog', kind: 'Definition type', gameplay: 'Rules and modes', policies: 'Policies',
    newRule: 'New definition', dialogHint: 'Choose an ID. An existing definition can serve as a template.', id: 'Definition ID',
    select: 'Select a definition to view its source.', empty: 'No definitions match this search.',
    form: 'Fields', definition: 'Definition', source: 'Source JSON', latest: 'Current engine source', editing: 'Rule editing controls',
    delete: 'Delete definition', confirmDelete: 'Mark this definition for deletion? The engine checks its references before saving.',
    inherited: 'Inherited or read only', createReference: 'Create referenced definition',
    unset: 'Engine default', yes: 'Yes', no: 'No', editAdvanced: 'Edit this field in Advanced.', invalidJson: 'Enter a JSON object with the same definition ID.',
    'combat-rules': 'Combat rules', 'flow-rules': 'Flow rules', 'phase-sequences': 'Phase sequences', modes: 'Game modes',
    'capability-policies': 'Capability policies', 'content-binding-policies': 'Content binding', 'replay-policies': 'Replay', 'timeline-policies': 'Timeline',
  },
  'pt-BR': {
    intro: 'Configure regras de combate, fases do turno, modos de jogo e políticas.',
    rules: 'Catálogo de regras', kind: 'Tipo de definição', gameplay: 'Regras e modos', policies: 'Políticas',
    newRule: 'Nova definição', dialogHint: 'Informe um ID. Você pode usar uma definição existente como exemplo.', id: 'ID da definição',
    select: 'Selecione uma definição para consultar a fonte.', empty: 'Nenhuma definição corresponde à busca.',
    form: 'Campos', definition: 'Definição', source: 'JSON fonte', latest: 'Fonte atual da engine', editing: 'Controles de edição de regras',
    delete: 'Excluir definição', confirmDelete: 'Marcar esta definição para exclusão? A engine verifica as referências antes de salvar.',
    inherited: 'Herdada ou somente leitura', createReference: 'Criar definição de referência',
    unset: 'Padrão da engine', yes: 'Sim', no: 'Não', editAdvanced: 'Edite este campo em Avançado.', invalidJson: 'Informe um objeto JSON com o mesmo ID da definição.',
    'combat-rules': 'Regras de combate', 'flow-rules': 'Regras de fluxo', 'phase-sequences': 'Sequências de fases', modes: 'Modos de jogo',
    'capability-policies': 'Permissões', 'content-binding-policies': 'Vínculo de conteúdo', 'replay-policies': 'Replay', 'timeline-policies': 'Linha do tempo',
  },
} as const
export type RuleCopy = (typeof ruleCopy)[keyof typeof ruleCopy]

const portuguese: Record<string, string> = {
  name: 'Nome', description: 'Descrição', version: 'Versão', flow: 'Fluxo de combate', turnOrder: 'Ordem de turno',
  automaticResolution: 'Resolução automática', maxAutomaticSteps: 'Máximo de etapas automáticas',
  strategy: 'Estratégia', recalculateAt: 'Recalcular em', tieBreak: 'Desempate', ai: 'IA',
  actionBudget: 'Limite de ações', resourceId: 'Recurso', enabled: 'Ativado', allowMapNavigation: 'Permitir navegação no mapa',
  defaultPhaseSequenceId: 'Sequência de fases padrão', entryPhaseId: 'Fase inicial', phases: 'Fases',
  phaseId: 'ID da fase', maxAutomaticTransitions: 'Máximo de transições automáticas', edges: 'Transições',
  edgeId: 'ID da transição', targetPhaseId: 'Fase de destino', trigger: 'Gatilho', condition: 'Condição',
  priority: 'Prioridade', order: 'Ordem', role: 'Papel da fase', allowPriority: 'Permitir prioridade',
  allowedActions: 'Ações permitidas', allowedCommandTags: 'Tags de comando permitidas',
  commandTags: 'Tags de comando', actionTypes: 'Tipos de ação',
  runDefinitionId: 'Definição da run', flowRulesId: 'Regra de fluxo', combatRulesId: 'Regra de combate',
  replayPolicyId: 'Política de replay', timelinePolicyId: 'Política de linha do tempo',
  contentBindingPolicyId: 'Política de vínculo de conteúdo', capabilityPolicyId: 'Política de permissões',
  cardZoneSystemId: 'Sistema de zonas de cartas', cardPoolIds: 'Pools de cartas', enemyPoolIds: 'Pools de inimigos',
  calculationPipelineIds: 'Pipelines de cálculo', allowCustomSeed: 'Permitir seed personalizada',
  activeRuns: 'Runs ativas', activationBoundary: 'Limite de ativação', timelineAccess: 'Acesso à linha do tempo',
  semanticVerification: 'Verificação semântica', groupByTurn: 'Agrupar por turno', maxItemsPerPage: 'Máximo de itens por página',
  allowHistoricalInspection: 'Permitir inspeção do histórico', allowForkFromHistory: 'Permitir branch a partir do histórico',
  allowHeadRestore: 'Permitir restauração da versão atual',
}

export function fieldLabel(key: string, locale: string) {
  if (locale === 'pt-BR' && portuguese[key]) return portuguese[key]
  return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (letter) => letter.toUpperCase())
}
