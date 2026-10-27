# Loadout unificado na Godot

Implementado em 27/10/2026. [Conceito](../design/UNIFIED_LOADOUT_CONCEPT_AND_RESEARCH.md) e [etapas/validação](../plans/UNIFIED_LOADOUT_IMPLEMENTATION.md).

## Acesso e guias

O botão **Loadout** no mapa e nas atividades abre a página do personagem atual. Os antigos acessos contextuais de inscrições/compartimentos apontam para a mesma página. Voltar usa a navegação da run; o mapa mantém seu estado de leitura pelo mecanismo existente. Não há seletor de personagem/setting ou fallback de dados offline na página da run.

- **Visão geral:** contagens e atalhos para os mesmos dados das outras guias.
- **Inscrições:** coleção real, encaixes, orçamento, busca, filtros e detalhes. Equipar/remover valida e salva automaticamente.
- **Baralho:** cartas canônicas com o mesmo CardView e inspector do combate; busca, filtro de tags/resources e ordenação por nome/custo. Recursos diferentes não são somados numa nota de custo/poder.
- **Compartimentos**, dentro de Baralho: aparece quando a política estiver habilitada. Selecione uma carta e um destino, ou arraste até o destino. O movimento completo salva automaticamente; não há Apply na página unificada.
- **Modificadores de cartas**, dentro de Baralho: inventário e opções de aplicação anunciadas pela engine. Selecionar uma aplicação elegível valida e envia a ação canônica; a página não concede crafting em locais onde não esteja permitido.
- **Relíquias:** leitura das relíquias obtidas e de suas descrições, sem inventar slots ou permitir desequipar livremente.
- **Companions:** seção informativa marcada Planejado, sem equipamento, inventário, progressão ou gravação fictícios.

O painel compartilhado apresenta os resources e atributos retornados pela engine. A inspeção de origens mostra contribuições e valores de entrada/saída, sem refazer cálculos. Em largura reduzida, o painel vira uma gaveta sobreposta, sem reservar coluna vazia.

## Contrato de salvamento

Cada operação completa segue leitura atual → preview quando aplicável → comando existente → releitura confirmada. Inscrições usam `APPLY_LOADOUT`; compartimentos usam `APPLY_DECK_COMPARTMENTS`; itens modificadores usam a opção `APPLY_CARD_MODIFIER_ITEM` anunciada. Trocar guia, inspecionar, buscar, filtrar ou iniciar/cancelar um drag não grava.

O coordenador da página mantém as operações enquanto uma guia fica oculta. Bloqueia edições concorrentes da mesma run; a próxima operação relê a versão atual. Não há Salvar tudo nem transação global entre inventários. Uma troca de posições de inscrições continua um único payload final, validado pela engine.

Voltar durante uma gravação mostra a pendência e impede o fechamento prematuro. Resposta incerta oferece recuperação pelo comando pendente da sessão, com a mesma identidade. Preview inválido, conflito ou payload malformado não vira sucesso visual. Leituras de versões/personagens/runs diferentes não são combinadas. Não há timer de polling por guia.

**Desequipar tudo/Limpar distribuição** informa a ação de limpeza em massa e pede confirmação da ação; a gravação posterior é automática. Isso não é um botão de salvar rascunho.

## Componentes e fronteiras

- `unified_loadout_screen.gd`: composição da página e intenções visuais.
- `loadout_page_view.gd`: cabeçalho, guias, conteúdo persistente e atributos.
- `unified_loadout_controller.gd`: contexto/versão, leitura e sequenciamento dos serviços existentes.
- `loadout_workspace.gd` e `deck_compartment_workspace.gd`: candidatos locais enviados ao preview, nunca fonte de atributos ou regras.
- `deck_module_view.gd`: grade com CardView reutilizado, destinos e inspeção. Alterações de versão sem mudança visual não recriam as cartas.
- `loadout_presenter.gd`: formatação das projeções, sem RNG ou regras.
- `game_session.gd`/`engine_gateway.gd`: REST, validação de contexto, comandos e recuperação.

As únicas novas chamadas são leituras de endpoints já existentes para relíquias e atributos. Não foram alterados Core, regras, formatos de save, versão determinística ou protocolos de comandos da engine.

## Laboratórios e testes

O menu **Loadout Lab** abre `scenes/unified_loadout_lab.tscn`, isolado e identificado como visual. Reutiliza os componentes e fixtures existentes, sem salvar na engine. O laboratório específico de inscrições e o editor específico de compartimentos permanecem disponíveis em suas cenas para testes técnicos; o editor antigo de compartimentos conserva seu Apply explícito somente nesse fluxo de laboratório.

Testes offline novos:

```powershell
./tools/test-godot-ui.ps1 -Tests unified_loadout_shell,unified_loadout_autosave,unified_loadout_recovery,unified_loadout_info_contract,unified_loadout_layout
```

Capturas podem ser geradas pelo teste `unified_loadout_layout` em um renderer real, com `--lab-output=<pasta>`. A matriz cobre EN/PT-BR, texto ampliado e 1280×720, 1920×1080, 2560×1080.

`unified_loadout_online.gd` exige **API isolada em 127.0.0.1:61989**, com diretórios de persistência próprios e perfil de ferramentas `dev_modder` para verificar replay. O teste recusa as portas normais do jogador; cria runs de QA em Ascendant e Deck Workshop e verifica autosave, navegação, reentrada e replay. Passe `-ApiUrl http://127.0.0.1:61989` ao runner. Não aponte os diretórios dessa API para saves reais.

Presets da build inteira e companions funcionais continuam fora do escopo. Autorizações de edição e disponibilidade de conteúdo permanecem definidas pelo setting e pelo servidor.
