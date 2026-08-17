# Contratos OpenAPI publicados

`heroscript-v1.json` é a cópia revisável do contrato público inicial. Em
execução, a mesma versão é disponibilizada em `/openapi/v1.json`; o endpoint
histórico `/swagger/v1/swagger.json` existe apenas para compatibilidade.

A especificação estática é deliberadamente focada na superfície autoritativa
v1. Rotas legadas e administrativas são classificadas na documentação, mas não
são tratadas como contrato de integração estável. CI valida a estrutura do
arquivo e a presença das rotas críticas; testes de integração validam o
comportamento real.
