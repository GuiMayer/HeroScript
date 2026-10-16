# Navegação do mapa — implementação e certificação

## Etapa 1: baseline e contrato

Baseline estático do mundo 1: 50 nós, 25 camadas, seis alternativas na camada mais larga; perfil anterior aproximadamente 1710 × 2696 unidades. A coluna permanente retirava mais 300 unidades da viewport. Os testes anteriores exigiam scroll horizontal mesmo para um diamante simples.

MapNavigationModel separa seleção, hover, foco e posição autoritativa. Somente `travel` publicado torna um destino uma alternativa; arestas não concedem permissões. O modelo copia os dados e não possui acesso à sessão, transporte ou RNG.

Não foram abertas nem modificadas partidas pessoais. Capturas históricas não são apresentadas como playtest atual. Certificação visual e de integração será registrada nas etapas seguintes.

## Etapa 2: geometria compacta

Símbolos de 42, hitboxes de 72 × 64 e células de 112 × 104. Seis ramos ocupam 800 unidades incluindo margens, contra 1710 anteriormente. Camadas centralizadas; quatro passagens baricêntricas com desempate por ID. Rótulos ficam abaixo dos símbolos e as conexões saem após a área reservada ao texto. SCC, ciclos, desconexões e cache por topologia foram preservados. Testes iniciais: 35 checks aprovados nos quatro scripts de mapa, antes das duas novas asserções de seis ramos/sobreposição.
