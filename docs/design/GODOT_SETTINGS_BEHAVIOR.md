# Contrato de configurações do cliente Godot

Atualizado em 2026-10-11 após a auditoria das seis categorias do menu. São preferências de apresentação/input/conexão, não regras de gameplay.

## Carregamento e recuperação

- `SettingsSchema` define tipos, enumerações e limites numéricos usados tanto pelo controller quanto pelo carregamento do arquivo.
- Valores numéricos finitos são limitados ao intervalo permitido. Tipos incorretos e valores não finitos usam o valor inicial seguro.
- Idioma desconhecido é normalizado para `en` nas preferências, não apenas na tradução. Salvar opções sem mudar o idioma não emite uma troca de idioma.
- Um arquivo válido com opções inválidas recupera cada opção independentemente. Recuperação não grava automaticamente o arquivo; a próxima gravação persiste os valores recuperados.

## Controles

- Teclas vazias, desconhecidas, modificadas e reservadas não podem ser remapeadas. Botões de gamepad devem estar no intervalo aceito pelo Godot e não ser reservados à navegação.
- Ao carregar, os bindings válidos e não conflitantes são reservados primeiro. Entradas inválidas ou duplicadas recebem um padrão disponível, sem roubar bindings personalizados válidos.
- Se um padrão de uma ação já estiver ocupado por outra personalização válida, a recuperação escolhe outro padrão livre. Não garante o atalho original de cada ação; garante controles válidos e únicos por dispositivo.
- “Reset Controls” restaura os padrões de teclado e gamepad. A substituição reconstrói os eventos do dispositivo correspondente, removendo inclusive eventos vazios que o matching individual do Godot não conseguia apagar.
- Atalhos de navegação da interface permanecem independentes dos atalhos de gameplay.

## Áudio

- Master, música e efeitos sonoros continuam independentes. Música/SFX possuem ganho próprio, multiplicado pelo ganho do Master.
- 0% significa ganho linear exatamente zero, não um piso de −60 dB. Master também fica explicitamente mutado em zero.
- Silenciar música não reinicia nem interrompe sua posição de reprodução. Restaurar o volume mantém a continuidade da faixa.

## Conexão

- O endereço precisa ser HTTP/HTTPS, com host válido e porta opcional entre 1 e 65535. Aceita localhost, IPv4, IPv6 entre colchetes e caminho-base para proxy.
- Normalização remove espaços externos/barras finais e padroniza esquema/host. Rejeita whitespace interno, host vazio, portas inválidas, credenciais embutidas, query/fragment e barras invertidas. Valida também endereços salvos e overrides de linha de comando.
- Validação de sintaxe não afirma que o servidor está acessível. A disponibilidade só é estabelecida pela comunicação.
- Uma operação em andamento ou um comando de resultado incerto impede trocar o endereço. Não descarta uma escrita incerta para permitir a troca.
- Ao trocar de endpoint, sessão, projeções/pilhas, cursor de apresentação e disponibilidade antiga são invalidados. O transporte descarta respostas atrasadas de um endpoint anterior, sem substituir o erro/disponibilidade do atual.
- O menu mostra “ENGINE OFFLINE” enquanto o servidor não foi verificado, e oferece “RECONNECT”. “CONNECTING” aparece apenas durante uma tentativa. Reconectar sem run consulta os settings; não cria uma partida. Com uma run/comando pendente, mantém o fluxo existente de recuperação.

## Replay

O intervalo entre comandos gravados é dividido pela velocidade escolhida. A base é 0,7 s normalmente e 0,18 s com movimento reduzido. O cursor, commits, frames e estado gravados não são alterados.

## Regressões automatizadas

```powershell
./tools/test-godot-ui.ps1 -Tests settings_contracts,settings_interaction,settings_recovery,connection_epoch,layers,resolutions,hand_ux,component_context,theme_contracts,pause_layers,ivory_menu,ivory_menus,setting_progression,view_replacement
./tools/test-godot-ui.ps1 -Tests settings_contracts -VisualTest settings_recovery -VisualOutputDirectory .runtime/settings-recovery-native
```

- `settings_interaction`: sliders reais, foco/categoria, persistência e idiomas em entradas pelo menu e pela pausa.
- `settings_recovery`: arquivos inválidos, preservação/reset de bindings, ganho zero, endereços inválidos, bloqueio de comandos pendentes, estado de reconexão e reprodução real do replay em velocidades distintas.
- `connection_epoch`: servidor HTTP local de teste; respostas atrasadas 200/503 não alteram o estado do novo endpoint.
- As demais suites protegem geometria/resoluções, mão, temas, pausa, progressão por setting e substituição de composição.

O runner utiliza diretórios de projeto/preferências isolados. As regressões não enviam comandos de gameplay à engine. O teste HTTP usa somente um servidor loopback próprio. Eventos de teclado/gamepad são simulados; não certificam hardware físico, qualidade audível nem disponibilidade de servidores externos.

Verificação em 2026-10-11: 658 checks aprovados na execução ampla, incluindo regressões com janela real; `settings_recovery` e `connection_epoch` também passaram separadamente em headless. A importação do projeto original apresentou o encerramento nativo intermitente `0xc0000005` já observado neste Godot 4.7.2, sem erros de script; a repetição encerrou com código 0 e stderr vazio. Permanecem os avisos antigos de ObjectDB em algumas suites anteriores; não foram ocultados nem tratados como falhas funcionais das configurações.
