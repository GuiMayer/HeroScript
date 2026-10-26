# Implementação das animações inspiradas no UNO Clash

Guia: `UNO_INSPIRED_CARD_MOTION_ARCHITECTURE_AND_PLAN.md`.

As alterações são exclusivamente de apresentação/input da Godot. Durações de ciclo, disposição da interface e fatos da engine são preservados. Commits contêm apenas o delta desta implementação; alterações anteriores do workspace permanecem separadas.

## Etapas

1. Baseline: rastreador local de marcos e cenários comparáveis com 2/5/10 cartas, executando uso, retenção e compra pelos componentes reais. Validação em andamento.
2. Pose completa e propriedade de transformação: pendente.
3. Curvas e orçamento compartilhados: pendente.
4. Hover/reorganização: pendente.
5. Arraste/retorno: pendente.
6. Feedback pendente e recibo: pendente.
7. Uso/contato: pendente.
8. Compra/lotes: pendente.
9. Interrupções/replay: pendente.
10. Certificação: pendente.

## Validação

Os testes usam cópia isolada do projeto Godot e diretório de usuário separado. Capturas, logs e dados de teste ficam em `.runtime`, sem alterar saves do jogador.
