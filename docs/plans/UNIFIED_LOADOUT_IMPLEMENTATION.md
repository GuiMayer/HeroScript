# Implementação do loadout unificado

Guia: [conceito aprovado](../design/UNIFIED_LOADOUT_CONCEPT_AND_RESEARCH.md). Todas as guias editáveis salvam automaticamente por operação completa; a engine continua a validar e aplicar comandos. Não há transação global fictícia nem mudança no determinismo.

## Etapas

1. Registrar conceito, decisões e baseline. Concluída: inscrições já usam autosave; compartimentos ainda usam Apply; há alterações locais de outras tarefas que serão preservadas fora destes commits.
2. Moldura, registro de módulos e laboratório isolado.
3. Inscrições reais e atributos canônicos compartilhados.
4. Baralho, compartimentos com autosave e modificadores autorizados.
5. Visão geral e relíquias como leituras dos mesmos dados.
6. Companions planejado e navegação unificada na run.
7. Localização, acessibilidade, foco e validação visual responsiva.
8. Regressões, API real, recuperação e documentação final.

## Verificação prevista

- Navegar/inspecionar não envia mutações; contextos de run/personagem/setting não se misturam.
- Preview/comando/releitura usam o gateway e os workspaces existentes; valores não são calculados na interface.
- Uma alteração completa produz um comando, inclusive uma troca de posições.
- Trocar guia não abandona gravação; voltar aguarda confirmação ou recuperação.
- Falhas e conflitos preservam o último estado confirmado; retry incerto usa o mesmo comando da sessão.
- EN/PT-BR, mouse/teclado, 1280×720, 1920×1080 e 2560×1080; laboratório separado da run.
- API real: persistência, read-only, retry e reentrada; testes existentes de loadout/compartimentos preservados.
