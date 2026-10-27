# Integração do duelo com HeroScript

Este cliente apresenta o setting `uno-clash`, modo `uno_classic`, com dois participantes e `PerRound` explícito. O setting separado `circular-table` demonstra quatro participantes na API; esta página não fornece um layout de quatro assentos.

Inicie a API do projeto e sirva esta pasta com um servidor estático, por exemplo:

```powershell
python -m http.server 8080 --directory examples/uno-clash-web
```

Abra `http://localhost:8080/?api=http://localhost:5260`, ajustando o endereço à API em uso. A configuração de origens permitidas da API deve incluir essa origem.

A UI envia comandos e usa as ações legais para habilitar o fim do turno. `blockedCommands` fornece os motivos de compra obrigatória ou escolha de cor pendente; a autoridade dessas regras está no JSON e na engine. O teste do contrato do botão é executado com:

```powershell
node --test examples/uno-clash-web/tests/mandatory-draw.test.cjs
```

Consulte `docs/content/circular-table-flow.md` para autoria, migração e limites de compatibilidade.
