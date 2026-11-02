# Contrato das cenas de interface

A cena possui a árvore estática. O script vincula referências, preenche conteúdo e traduz input em intenção. Controllers, sessão e engine continuam possuindo operações de gameplay.

## Montagem

1. Instanciar a PackedScene canônica. O registro scene_registry.gd centraliza as rotas adotadas, sem criar um segundo router.
2. Configurar contexto e dependências pelo contrato existente de setup/mount. Se a chamada precede add_child, resolver nodes por binding explícito; não depender de @onready já executado.
3. Anexar à árvore e iniciar somente os comportamentos que exigem presença na árvore. Sinais são conectados idempotentemente.
4. Atualizar modelos defensivamente, sem remontar o shell nem sobrescrever estado local de outro contexto.
5. Na saída, cancelar trabalho visual e desconectar contexto/sessão. Respostas antigas não podem atualizar a view nova.

Nomes únicos usados pelos bindings são contratos internos. Renomeá-los exige atualizar seus consumidores e testes no mesmo commit.

## Coleções e estilos

Listas variáveis instanciam cenas de itens e reconciliam por identidade. Controles internos de Tree/ItemList continuam usando as APIs nativas. Algoritmos de fan, câmera, gráficos e efeitos não viram árvores estáticas.

Geometria fixa pertence ao editor. Métricas responsivas e cores semânticas permanecem nos recursos existentes; cada propriedade deve ter uma única autoridade. A troca de contexto precisa atualizar assinaturas mesmo quando os valores visuais equivalem.

## Preview

screen_catalog_workbench.tscn é um harness offline. Instancia os mesmos componentes usados pelas rotas, sem gateways de gameplay. O catálogo começa com a bancada de componentes e o laboratório de loadout já offline; isso não declara os módulos internos deste último migrados.

Novas telas só entram no catálogo quando sua view passiva puder receber fixtures sem iniciar o controller real. Não criar cópia visual, executar comandos ou salvar preferências para exibir um preview.

## Evidências por migração

- Cena canônica usada pelo runtime, testes e preview.
- Geometria e animações equivalentes à baseline.
- Shell e foco/scroll preservados entre updates.
- Dados de entrada e recursos compartilhados não mutados.
- Nenhuma árvore procedural alternativa para o componente convertido.
