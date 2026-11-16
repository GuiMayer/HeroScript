export const ruleKinds = [
  'combat-rules', 'flow-rules', 'phase-sequences', 'modes',
  'capability-policies', 'content-binding-policies', 'replay-policies', 'timeline-policies',
] as const
const effectKinds = [
  'actions', 'modifiers', 'status-effects', 'effect-resolution-policies', 'gambits', 'powers', 'keywords',
] as const
export type DefinitionKind = (typeof ruleKinds)[number] | (typeof effectKinds)[number]
export type DefinitionSection = 'rules' | 'effects'

export const definitionGroups = {
  rules: [ruleKinds.slice(0, 4), ruleKinds.slice(4)],
  effects: [effectKinds.slice(0, 5), effectKinds.slice(5)],
}

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
    rawInvalid: 'Enter a valid JSON object.', rawNotice: 'This catalog uses free-form JSON. The engine validates its structure.',
    invalidId: 'Enter a unique definition ID.', addItem: 'Add item', removeItem: 'Remove item',
    moreFields: 'More effect settings', resetField: 'Use engine default', value: 'Value', propertyName: 'Property name',
    textType: 'Text', numberType: 'Number', booleanType: 'Boolean', objectType: 'Object', arrayType: 'List', nullType: 'Null',
    retryReferences: 'Retry loading options',
    costSettings: 'Cost settings', resourceCosts: 'Resource costs',
    valueType: 'Value type', addProperty: 'Add property', renameProperty: 'Rename property',
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
    rawInvalid: 'Informe um objeto JSON válido.', rawNotice: 'Este catálogo usa JSON livre. A engine valida sua estrutura.',
    invalidId: 'Informe um ID de definição único.', addItem: 'Adicionar item', removeItem: 'Remover item',
    moreFields: 'Mais configurações do efeito', resetField: 'Usar padrão da engine', value: 'Valor', propertyName: 'Nome da propriedade',
    textType: 'Texto', numberType: 'Número', booleanType: 'Sim ou não', objectType: 'Objeto', arrayType: 'Lista', nullType: 'Nulo',
    retryReferences: 'Tentar carregar opções novamente',
    costSettings: 'Configuração do custo', resourceCosts: 'Custos de recursos',
    valueType: 'Tipo de valor', addProperty: 'Adicionar propriedade', renameProperty: 'Renomear propriedade',
    'combat-rules': 'Regras de combate', 'flow-rules': 'Regras de fluxo', 'phase-sequences': 'Sequências de fases', modes: 'Modos de jogo',
    'capability-policies': 'Permissões', 'content-binding-policies': 'Vínculo de conteúdo', 'replay-policies': 'Replay', 'timeline-policies': 'Linha do tempo',
  },
} as const
export type RuleCopy = Record<keyof typeof ruleCopy.en, string>

const effectCopy = {
  en: {
    intro: 'Configure actions, modifiers, status effects and how they resolve.',
    rules: 'Effects catalog', gameplay: 'Effects and behavior', policies: 'Free-form catalogs', editing: 'Effects editing controls',
    actions: 'Actions', modifiers: 'Modifiers', 'status-effects': 'Status effects',
    'effect-resolution-policies': 'Effect resolution policies', gambits: 'Gambits', powers: 'Powers', keywords: 'Keywords',
  },
  'pt-BR': {
    intro: 'Configure ações, modificadores, efeitos de status e sua resolução.',
    rules: 'Catálogo de efeitos', gameplay: 'Efeitos e comportamento', policies: 'Catálogos de JSON livre', editing: 'Controles de edição de efeitos',
    actions: 'Ações', modifiers: 'Modificadores', 'status-effects': 'Efeitos de status',
    'effect-resolution-policies': 'Políticas de resolução de efeitos', gambits: 'Gambitos', powers: 'Poderes', keywords: 'Palavras-chave',
  },
}

export function definitionCopy(locale: keyof typeof ruleCopy, section: DefinitionSection) {
  const labels = { ...ruleCopy[locale], ...effectCopy[locale] }
  return { copy: section === 'effects' ? labels : ruleCopy[locale], labels }
}

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
  displayName: 'Nome de exibição', actionType: 'Tipo de ação', cooldown: 'Recarga', action: 'Ação',
  actionId: 'Ação', cardDefinitionId: 'Carta', predicates: 'Predicados', targetSelector: 'Seleção de alvo',
  effects: 'Efeitos', effectId: 'ID do efeito', type: 'Tipo', costs: 'Custos', amount: 'Quantidade',
  target: 'Alvo', targetResource: 'Recurso do alvo', statusId: 'Efeito de status', operation: 'Operação',
  flatValue: 'Valor fixo', formula: 'Fórmula', cardZoneFlowId: 'Fluxo de zona de cartas', cardCount: 'Número de cartas',
  defaultDuration: 'Duração padrão', durationTickBoundary: 'Momento de reduzir a duração', triggers: 'Gatilhos',
  triggerId: 'ID do gatilho', boundary: 'Momento de ativação', influences: 'Influências',
  defaultStacks: 'Acúmulos padrão', maxStacks: 'Máximo de acúmulos', dispellable: 'Pode ser dissipado',
  componentId: 'ID do componente', capabilityIds: 'Permissões exigidas', effect: 'Efeito',
  selectionResourceId: 'Recurso para seleção de alvo', statusStacks: 'Acúmulos de status', statusDuration: 'Duração do status',
  formulaValue: 'Fórmula do valor', chainedEffects: 'Efeitos encadeados', resourceField: 'Campo do recurso',
  calculationChannel: 'Canal de cálculo', calculationPipelineId: 'Pipeline de cálculo', modifierId: 'Modificador',
  modifierStacks: 'Acúmulos do modificador', modifierDuration: 'Duração do modificador', chance: 'Probabilidade',
  repeat: 'Repetições', requiredTags: 'Tags obrigatórias', excludedTags: 'Tags excluídas', metadata: 'Metadados',
  sequenceMutation: 'Alteração da sequência', attributeMutation: 'Alteração de atributo', parameters: 'Parâmetros',
  condensationRecipeId: 'Receita de condensação', continuation: 'Continuação', randomInputs: 'Entradas aleatórias',
  alternativeCosts: 'Custos alternativos', optionId: 'ID da opção', allowOverdraft: 'Permitir saldo negativo',
  zonePayments: 'Pagamentos com cartas', paymentId: 'ID do pagamento', flowId: 'Fluxo do pagamento',
  costOptionId: 'Opção de custo alternativo', sourceZoneId: 'Zona de origem', targetZoneId: 'Zona de destino',
  count: 'Quantidade', allowResolvingCard: 'Permitir a carta em resolução', expression: 'Expressão',
  failureReason: 'Mensagem quando a condição falhar', relativeOffset: 'Posição relativa',
  minimumTargets: 'Mínimo de alvos', maximumTargets: 'Máximo de alvos', allowSelf: 'Permitir selecionar a si mesmo',
  cardZoneResolutionFlowId: 'Fluxo de destino da carta', channel: 'Canal', bucket: 'Grupo de cálculo', value: 'Valor',
}

export function fieldLabel(key: string, locale: string) {
  if (locale === 'pt-BR' && portuguese[key]) return portuguese[key]
  return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (letter) => letter.toUpperCase())
}
