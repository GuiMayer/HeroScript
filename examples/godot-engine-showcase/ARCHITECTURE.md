# Camadas do cliente Godot

## Direção das dependências

```text
ui/ → application/GameSession → engine/EngineGateway → engine/HTTPTransport → REST
            │
            └─ receipt_received → presentation/Playback → animações da UI

bootstrap.gd: instancia transporte/gateway e conecta sessão, playback e preferências
presentation/I18n: traduz mensagens apenas na borda visual
```

`ui/` contém o visual substituível. Nenhuma tela importa o gateway/transporte,
conhece URLs, instancia `HTTPRequest` ou monta envelopes de concorrência.
Não há autoload público de transporte: o nó HTTP é privado da composição.

## Responsabilidades

| Camada | Cuida de | Não pode fazer |
| --- | --- | --- |
| Transporte HTTP | conexão, JSON, timeout, status, medições limitadas | traduzir, ler preferências, chamar telas |
| Gateway da engine | rotas, queries, schemas, IDs de comando, normalização de payload | animar, calcular regras, salvar preferências |
| Sessão | snapshots, leitura coordenada, comandos, versão esperada e erros de sincronização | construir URLs, tocar áudio, traduzir mensagens, avançar animações |
| Escolhas de progressão | transformar opções anunciadas em escolhas enviáveis | inventar IDs, preços ou regras de upgrade |
| Playback | copiar frames do recibo, manter cursor e emitir frame apresentado | enviar comandos, recalcular efeitos, alterar snapshots |
| UI | seleção, layout, navegação, tradução, áudio, input e ritmo de apresentação | declarar o resultado de uma regra |
| Bootstrap | injetar gateway e ligar sinais às preferências/playback | implementar regras ou desenhar telas |

As projeções públicas da sessão são cópias profundas. Uma tela pode organizar
seus dados sem modificar os snapshots usados para sincronização e envio.
As projeções continuam tendo o formato dos read models da engine: esta divisão
desacopla transporte e ciclo de vida, não promete compatibilidade com qualquer
mudança futura de contrato de dados.

## Trocar o visual

1. Reutilize os serviços montados pelo `bootstrap.gd` e substitua os scripts e
   cenas de `ui/`. O `main.tscn` atual aponta para `ui/main.gd`.
2. Leia `GameSession.run`, `combat`, `legal_actions` e `activity_choices()`.
   Faça a seleção e renderização com os novos componentes visuais.
3. Envie `submit_candidate(candidate)` para uma escolha de combate e
   `submit_activity(choice)` para progressão. A sessão/gateway cuidam do
   payload, versão observada e identidade do comando.
4. Use `start_campaign`, `start_sandbox`, `continue_run`, `timeline`,
   `branch_tree`, `create_branch`, `verify` e `simulate_end_turn` como casos de
   uso. O editor de sandbox pode enviar JSON de cenário; não executa suas regras.
5. Observe `changed`, `failed`, `command_started`, `command_finished` e
   `availability_changed`. Erros estruturados são formatados por `I18n.error`
   somente quando apresentados. Diagnósticos crus do servidor são preservados.
6. Apresente `Playback.frames` e avance com `Playback.advance()`, ou substitua
   o playback por outro consumidor de `receipt_received`. Ritmo, pause e input
   bloqueado durante animações são políticas da apresentação, não da engine.

Nenhuma preferência gráfica deve entrar em um envelope de jogo. No fluxo
atual, a UI exibe o snapshot final autoritativo e anima os frames recebidos;
não simula estados intermediários por conta própria.

## Injeção e testes

`GameSession.configure(gateway)` recebe a dependência; `EngineGateway.new(transport)`
recebe o transporte. A sessão não depende de autoloads de preferência, áudio,
tradução ou visual. O teste `tests/layers.gd` instancia a sessão com um transporte
em memória e sem cena principal. Uma futura implementação alternativa de gateway
deve fornecer os métodos atualmente usados por `game_session.gd`.

O bootstrap é o único lugar que liga `Preferences.api_url` ao transporte e
persiste o ID da run aberta. Retomar uma branch também atualiza esse ID. Abrir
outra run limpa o cursor visual; isso não altera os commits da run anterior.

Uma falha no refresh após POST bem-sucedido não transforma o comando em
fracasso: ele já foi persistido. A sessão bloqueia input desatualizado até
`refresh()` e não reenvia automaticamente o comando com outro ID.

## Localização e fallback

Inglês é fonte e fallback explícito, inclusive na configuração nativa da Godot:

```jsonc
// en.json
{"NEW JOURNEY": "NEW JOURNEY"}
// pt_BR.json
{"NEW JOURNEY": "NOVA JORNADA"}
```

As chaves são as próprias mensagens em inglês; placeholders são mantidos.
`I18n.text("NEW JOURNEY")` resolve a tradução escolhida, depois inglês e, por
último, a própria chave. Catálogos ausentes não impedem carregar a interface.
`content.json` usa IDs de conteúdo estáveis e nomes por idioma; falta de nome
localizado prioriza o inglês antes do nome fornecido ou ID formatado.

Preferência válida previamente salva é respeitada. Locale não suportado volta
para `en`. Trocar idioma não modifica conteúdo publicado, snapshots ou receipts.

## Validação desta separação

- Testes offline de fallback, transporte injetado e dependências proibidas.
- Cópias defensivas, normalização de payload e versões esperadas.
- Commit bem-sucedido seguido de falha na leitura, bloqueio e recuperação.
- Playback sem chamadas à engine e sem mutação de run.
- Teste de UI com REST real: carta/alvo, idiomas, pause, frames e replay.
- Campanha completa, sandboxes, timeline, branches e simulações pelo mesmo
  serviço de aplicação usado nas telas.

O teste estático de fronteiras impede dependências de HTTP na UI e dependências
visuais na sessão/gateway. Ele complementa os testes funcionais; não substitui
uma revisão arquitetural ao introduzir novos subsistemas.
