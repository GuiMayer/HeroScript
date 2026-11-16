export const ruleKinds = [
  'combat-rules', 'flow-rules', 'phase-sequences', 'modes',
  'capability-policies', 'content-binding-policies', 'replay-policies', 'timeline-policies',
] as const
const effectKinds = [
  'actions', 'modifiers', 'status-effects', 'effect-resolution-policies', 'gambits', 'powers', 'keywords',
] as const
const resourceGroups = [
  ['resources', 'actor-resource-lifecycle-policies'],
  ['calculation-pipelines', 'formulas'],
] as const
const entityGroups = [
  ['entities', 'enemy-pools', 'supports', 'support-routines'],
  ['enemies', 'companions', 'races'],
] as const
const deckGroups = [
  ['card-pools', 'decks', 'deck-modules', 'deck-composition-policies', 'deck-compartment-profiles', 'deck-compartment-policies'],
  ['loadout-policies', 'support-loadout-policies', 'inscriptions'],
] as const
const runGroups = [
  ['runs', 'run-progression-policies', 'profile-progress-policies', 'daily-challenges'],
  ['dialogues', 'preparations', 'shops', 'relics', 'run-events', 'boards', 'zones'],
] as const
export type DefinitionKind = (typeof ruleKinds)[number] | (typeof effectKinds)[number] |
  (typeof resourceGroups)[number][number] | (typeof entityGroups)[number][number] | (typeof deckGroups)[number][number] |
  (typeof runGroups)[number][number]
export type DefinitionSection = 'rules' | 'effects' | 'resources' | 'entities' | 'decks' | 'runs'

export const definitionGroups = {
  rules: [ruleKinds.slice(0, 4), ruleKinds.slice(4)],
  effects: [effectKinds.slice(0, 5), effectKinds.slice(5)],
  resources: resourceGroups,
  entities: entityGroups,
  decks: deckGroups,
  runs: runGroups,
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
    rawInvalid: 'Enter a valid JSON object.', rawNotice: 'Add properties and choose their value types. This catalog has no required fields.',
    properties: 'Properties',
    invalidId: 'Enter a unique definition ID.', addItem: 'Add item', removeItem: 'Remove item',
    moreFields: 'More effect settings', resetField: 'Use engine default', value: 'Value', propertyName: 'Property name',
    textType: 'Text', numberType: 'Number', booleanType: 'Boolean', objectType: 'Object', arrayType: 'List', nullType: 'Null',
    retryReferences: 'Retry loading options',
    costSettings: 'Cost settings', resourceCosts: 'Resource costs',
    valueType: 'Value type', addProperty: 'Add property', renameProperty: 'Rename property',
    moveEarlier: 'Move earlier', moveLater: 'Move later',
    componentType: 'Component type', addComponent: 'Add component',
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
    rawInvalid: 'Informe um objeto JSON válido.', rawNotice: 'Adicione propriedades e escolha o tipo dos valores. Este catálogo não tem campos obrigatórios.',
    properties: 'Propriedades',
    invalidId: 'Informe um ID de definição único.', addItem: 'Adicionar item', removeItem: 'Remover item',
    moreFields: 'Mais configurações do efeito', resetField: 'Usar padrão da engine', value: 'Valor', propertyName: 'Nome da propriedade',
    textType: 'Texto', numberType: 'Número', booleanType: 'Sim ou não', objectType: 'Objeto', arrayType: 'Lista', nullType: 'Nulo',
    retryReferences: 'Tentar carregar opções novamente',
    costSettings: 'Configuração do custo', resourceCosts: 'Custos de recursos',
    valueType: 'Tipo de valor', addProperty: 'Adicionar propriedade', renameProperty: 'Renomear propriedade',
    moveEarlier: 'Mover para cima', moveLater: 'Mover para baixo',
    componentType: 'Tipo de componente', addComponent: 'Adicionar componente',
    'combat-rules': 'Regras de combate', 'flow-rules': 'Regras de fluxo', 'phase-sequences': 'Sequências de fases', modes: 'Modos de jogo',
    'capability-policies': 'Permissões', 'content-binding-policies': 'Vínculo de conteúdo', 'replay-policies': 'Replay', 'timeline-policies': 'Linha do tempo',
  },
} as const
export type RuleCopy = Record<keyof typeof ruleCopy.en, string>

const effectCopy = {
  en: {
    intro: 'Configure actions, modifiers, status effects and how they resolve.',
    rules: 'Effects catalog', gameplay: 'Effects and behavior', policies: 'Powers and keywords', editing: 'Effects editing controls',
    actions: 'Actions', modifiers: 'Modifiers', 'status-effects': 'Status effects',
    'effect-resolution-policies': 'Effect resolution policies', gambits: 'Gambits', powers: 'Powers', keywords: 'Keywords',
  },
  'pt-BR': {
    intro: 'Configure ações, modificadores, efeitos de status e sua resolução.',
    rules: 'Catálogo de efeitos', gameplay: 'Efeitos e comportamento', policies: 'Poderes e palavras-chave', editing: 'Controles de edição de efeitos',
    actions: 'Ações', modifiers: 'Modificadores', 'status-effects': 'Efeitos de status',
    'effect-resolution-policies': 'Políticas de resolução de efeitos', gambits: 'Gambitos', powers: 'Poderes', keywords: 'Palavras-chave',
  },
}

const resourceCopy = {
  en: {
    intro: 'Configure resources, their lifecycle, calculation pipelines and formulas.',
    rules: 'Resource and formula catalog', gameplay: 'Resources and lifecycle', policies: 'Calculations',
    editing: 'Resource and formula editing controls',
    resources: 'Resources', 'actor-resource-lifecycle-policies': 'Resource lifecycle policies',
    'calculation-pipelines': 'Calculation pipelines', formulas: 'Formulas',
  },
  'pt-BR': {
    intro: 'Configure recursos, seu ciclo de vida, pipelines de cálculo e fórmulas.',
    rules: 'Catálogo de recursos e fórmulas', gameplay: 'Recursos e ciclo de vida', policies: 'Cálculos',
    editing: 'Controles de edição de recursos e fórmulas',
    resources: 'Recursos', 'actor-resource-lifecycle-policies': 'Políticas de ciclo de vida dos recursos',
    'calculation-pipelines': 'Pipelines de cálculo', formulas: 'Fórmulas',
  },
}

const entityCopy = {
  en: {
    intro: 'Configure actors, enemy pools, supports and their routines.',
    rules: 'Entity catalog', gameplay: 'Entities and support', policies: 'Open catalogs', editing: 'Entity editing controls',
    entities: 'Entities', enemies: 'Enemies', 'enemy-pools': 'Enemy pools', companions: 'Companions',
    races: 'Races', supports: 'Supports', 'support-routines': 'Support routines',
  },
  'pt-BR': {
    intro: 'Configure atores, grupos de inimigos, suportes e suas rotinas.',
    rules: 'Catálogo de entidades', gameplay: 'Entidades e suporte', policies: 'Catálogos de propriedades livres',
    editing: 'Controles de edição de entidades', entities: 'Entidades', enemies: 'Inimigos',
    'enemy-pools': 'Grupos de inimigos', companions: 'Companheiros', races: 'Raças', supports: 'Suportes',
    'support-routines': 'Rotinas de suporte',
  },
}

const deckCopy = {
  en: {
    intro: 'Configure card pools, deck composition, compartments and equipment.',
    rules: 'Deck and pool catalog', gameplay: 'Decks, pools and compartments', policies: 'Equipment',
    editing: 'Deck and pool editing controls',
    decks: 'Decks', 'card-pools': 'Card pools', 'deck-modules': 'Deck modules', 'deck-composition-policies': 'Deck composition policies',
    'deck-compartment-profiles': 'Deck compartment profiles', 'deck-compartment-policies': 'Deck compartment policies',
    'loadout-policies': 'Equipment policies', 'support-loadout-policies': 'Support equipment policies', inscriptions: 'Inscriptions',
  },
  'pt-BR': {
    intro: 'Configure pools de cartas, composição de decks, compartimentos e equipamentos.',
    rules: 'Catálogo de decks e pools', gameplay: 'Decks, pools e compartimentos', policies: 'Equipamentos',
    editing: 'Controles de edição de decks e pools',
    decks: 'Decks', 'card-pools': 'Pools de cartas', 'deck-modules': 'Módulos de deck', 'deck-composition-policies': 'Políticas de composição',
    'deck-compartment-profiles': 'Perfis de compartimentos', 'deck-compartment-policies': 'Políticas de compartimentos',
    'loadout-policies': 'Políticas de equipamento', 'support-loadout-policies': 'Políticas de equipamento de suporte', inscriptions: 'Inscrições',
  },
}

const runCopy = {
  en: {
    intro: 'Configure run maps, progression, activities and rewards.',
    rules: 'Run and progression catalog', gameplay: 'Runs and policies', policies: 'Activities and content',
    editing: 'Run and progression editing controls',
    runs: 'Runs', 'run-progression-policies': 'Run progression policies', 'profile-progress-policies': 'Profile progression policies',
    'daily-challenges': 'Daily challenges', dialogues: 'Dialogues', preparations: 'Preparations', shops: 'Shops',
    relics: 'Relics', 'run-events': 'Run events', boards: 'Boards', zones: 'Zones',
  },
  'pt-BR': {
    intro: 'Configure mapas de runs, progressão, atividades e recompensas.',
    rules: 'Catálogo de runs e progressão', gameplay: 'Runs e políticas', policies: 'Atividades e conteúdo',
    editing: 'Controles de edição de runs e progressão',
    runs: 'Runs', 'run-progression-policies': 'Políticas de progressão da run', 'profile-progress-policies': 'Políticas de progressão do perfil',
    'daily-challenges': 'Desafios diários', dialogues: 'Diálogos', preparations: 'Preparações', shops: 'Lojas',
    relics: 'Relíquias', 'run-events': 'Eventos da run', boards: 'Tabuleiros', zones: 'Zonas',
  },
}

export function definitionCopy(locale: keyof typeof ruleCopy, section: DefinitionSection) {
  const labels = { ...ruleCopy[locale], ...effectCopy[locale], ...resourceCopy[locale], ...entityCopy[locale], ...deckCopy[locale], ...runCopy[locale] }
  const extra = { rules: {}, effects: effectCopy[locale], resources: resourceCopy[locale],
    entities: entityCopy[locale], decks: deckCopy[locale], runs: runCopy[locale] }[section]
  return { copy: { ...ruleCopy[locale], ...extra }, labels }
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
  shortName: 'Nome curto', category: 'Categoria', defaultMin: 'Mínimo padrão', defaultMax: 'Máximo padrão',
  defaultCurrent: 'Valor inicial', canBeNegative: 'Permitir valor negativo', canExceedMax: 'Permitir exceder o máximo',
  costMultiplier: 'Multiplicador de custo', regeneration: 'Regeneração', amountPerTurn: 'Quantidade por turno',
  timing: 'Momento da regeneração', thresholdPolicies: 'Políticas de limite', thresholdSource: 'Origem do limite',
  thresholdValue: 'Valor do limite', tolerance: 'Tolerância', comparison: 'Comparação', consequence: 'Consequência',
  rules: 'Regras', entry: 'Entrada', victory: 'Vitória', defeat: 'Derrota', draw: 'Empate', abandoned: 'Abandono',
  retry: 'Nova tentativa', configuredValue: 'Valor configurado', missingResource: 'Quando o recurso estiver ausente',
  unitId: 'Unidade', stages: 'Etapas', stageId: 'Etapa', scope: 'Escopo', buckets: 'Grupos de cálculo',
  bucketId: 'ID do grupo', rounding: 'Arredondamento', midpointRounding: 'Arredondamento de empates',
  resourceInfluenceBindings: 'Influências de recursos', statInfluenceBindings: 'Influências de atributos',
  params: 'Parâmetros', parameterConstraints: 'Restrições de parâmetros', minimumExclusive: 'Mínimo exclusivo',
  operations: 'Operações', op: 'Operação', operands: 'Operandos', min: 'Mínimo', max: 'Máximo',
  minimum: 'Mínimo', maximum: 'Máximo',
  components: 'Componentes', resources: 'Recursos', pools: 'Reservas de recursos', current: 'Valor atual',
  values: 'Valores', valueRules: 'Regras de atributos', stats: 'Atributos', inventory: 'Inventário',
  startingItems: 'Itens iniciais', capacity: 'Capacidade', abilities: 'Habilidades', abilityIds: 'Ações disponíveis',
  deck_compartments: 'Compartimentos do deck', profileId: 'Perfil de compartimentos', responses: 'Respostas',
  entityDefinitionId: 'Entidade', entityDefinitionIds: 'Entidades', countsForOutcome: 'Conta para o resultado',
  ownerActivationInterval: 'Intervalo de ativações do dono', routineIds: 'Rotinas permitidas',
  defaultRoutineId: 'Rotina padrão', gambitIds: 'Gambitos', iconPath: 'Caminho do ícone', spritePath: 'Caminho do sprite',
  explicitCardIds: 'Cartas incluídas', includeTags: 'Tags de inclusão', excludeTags: 'Tags de exclusão',
  rarity: 'Raridade', rarityWeights: 'Pesos de raridade', editWindows: 'Momentos em que é possível editar',
  equipmentWindows: 'Momentos em que é possível equipar', groups: 'Grupos', groupId: 'ID do grupo',
  cardIds: 'Cartas permitidas', startingCardIds: 'Cartas iniciais', allowDuplicateDefinitions: 'Permitir cópias da mesma definição',
  moduleIds: 'Módulos permitidos', startingModuleId: 'Módulo inicial', compartments: 'Compartimentos', compartmentId: 'ID do compartimento',
  grants: 'Componentes concedidos', grantId: 'ID da concessão', bundleId: 'Pacote de componentes',
  effectBindings: 'Vínculos de efeitos', grantedTags: 'Tags concedidas', preparationOnly: 'Apenas na preparação',
  inscriptionIds: 'Inscrições permitidas', startingInscriptionIds: 'Inscrições iniciais', slots: 'Slots', slotId: 'ID do slot',
  supportIds: 'Suportes permitidos', startingOwnedSupportIds: 'Suportes inicialmente possuídos', startingSupportId: 'Suporte equipado inicialmente',
  inventoryScope: 'Escopo do inventário', capacityOnly: 'Equipamento por capacidade', allowTransfers: 'Permitir transferências',
  maximumInventoryEntries: 'Limite de itens no inventário', stations: 'Estações', stationId: 'ID da estação',
  offerCount: 'Quantidade de ofertas', maximumPurchases: 'Máximo de compras', pool: 'Pool de ofertas',
  allowedActorDefinitionIds: 'Entidades autorizadas', attributeBonuses: 'Bônus de atributos', attributeId: 'Atributo',
  rank: 'Nível', ranks: 'Níveis', equipmentCost: 'Custo de equipamento', polarity: 'Polaridade',
  revisionMigration: 'Migração de revisão', influenceValues: 'Valores de influência', presentation: 'Apresentação',
  playerDefinitionId: 'Entidade jogadora', startingResources: 'Recursos iniciais', startingCards: 'Cartas iniciais',
  initialPlayableCardCount: 'Cartas jogáveis no início', initialCardZoneId: 'Zona inicial das cartas', initialCardOwner: 'Dono inicial das cartas',
  mapNodes: 'Nós do mapa', nodeId: 'ID do nó', nextNodeIds: 'Próximos nós', activity: 'Atividade',
  completionPolicy: 'Política de conclusão', entryEffects: 'Efeitos de entrada', exitEffects: 'Efeitos de saída',
  entryEffectOwner: 'Dono dos efeitos de entrada', exitEffectOwner: 'Dono dos efeitos de saída',
  encounterVictory: 'Vitória no encontro', encounterDefeat: 'Derrota no encontro', encounterDraw: 'Empate no encontro',
  encounterAbandoned: 'Abandono do encontro', encounterRetry: 'Nova tentativa de encontro',
  retryableEncounterOutcomes: 'Resultados que permitem tentar novamente', endOfMap: 'Fim do mapa',
  allowAbandon: 'Permitir abandonar a run', allowOutOfActivityCommands: 'Permitir comandos fora da atividade',
  contributingModeIds: 'Modos que contam para a progressão', allowedProvenances: 'Origens permitidas', deduplication: 'Contagem de runs',
  unlocks: 'Desbloqueios', unlockId: 'ID do desbloqueio', targets: 'Itens desbloqueados', outcomes: 'Resultados da run',
  encounterOutcomes: 'Resultados do encontro', isCurrent: 'Desafio atual', configName: 'Game setting',
  scoreResourceWeights: 'Pesos dos recursos na pontuação', modeId: 'Modo de jogo', seed: 'Seed',
  title: 'Título', startNodeId: 'Nó inicial', nodes: 'Nós', speaker: 'Quem fala', translations: 'Traduções',
  choices: 'Escolhas', choiceId: 'ID da escolha', interactionKind: 'Tipo de interação', nextNodeId: 'Próximo nó',
  hideWhenUnavailable: 'Ocultar quando indisponível', unavailableText: 'Mensagem de indisponibilidade', once: 'Apenas uma vez',
  setFlags: 'Variáveis definidas pela escolha', items: 'Itens', pricing: 'Preços', reroll: 'Renovação das ofertas',
  baseMultiplier: 'Multiplicador base', rarityMultipliers: 'Multiplicadores por raridade', tagMultipliers: 'Multiplicadores por tag',
  baseCosts: 'Custos base', costsPerReroll: 'Custos por renovação', grantedCardIds: 'Cartas concedidas',
  applyModifiers: 'Modificadores aplicados', resourceRestrictions: 'Restrições de recursos', currentCeiling: 'Teto do valor atual',
  stackLimit: 'Limite de acúmulos', reactions: 'Reações',
}

export function fieldLabel(key: string, locale: string) {
  if (locale === 'pt-BR' && portuguese[key]) return portuguese[key]
  if (key === 'op') return 'Operation'
  if (key === 'params') return 'Parameters'
  return key.replaceAll('_', ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^./, (letter) => letter.toUpperCase())
}
