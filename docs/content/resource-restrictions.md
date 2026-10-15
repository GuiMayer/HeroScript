# Restrições de recurso por dono

Relíquias podem declarar `resourceRestrictions`. A regra identifica um recurso, um teto de saldo e uma política explícita para o saldo existente. Não altera o máximo base nem sabe o significado de shield, block, vida ou armor.

```json
{
  "relicId": "example",
  "resourceRestrictions": [{
    "ruleId": "disable_barrier",
    "resourceId": "barrier",
    "currentCeiling": 0,
    "existingBalance": "DiscardExcess"
  }]
}
```

O teto é materializado em `ResourcePool.currentCeiling`. Sem regra, esse campo é ausente na serialização. A fonte autoritativa continua sendo a definição capturada na instância de relíquia; materialização não acumula alterações no máximo ou no saldo base. Várias restrições aplicáveis se intersectam usando o menor teto, independentemente da ordem. Uma restrição abaixo do mínimo efetivo é rejeitada atomicamente. Recursos ausentes no dono são ignorados; a definição do recurso precisa existir no catálogo.

`DiscardExcess` é a única política implementada neste marco e precisa ser declarada. Descarta imediatamente o saldo excedente ao adquirir/materializar a regra. Remover a última restrição libera o recurso, mas não devolve o saldo descartado. Preservar saldo inacessível e converter saldo para outro atributo não são comportamentos implícitos nem opções disponíveis.

Aquisição/remoção atualizam o personagem persistente e encontros ainda não resolvidos. Histórico de encontros resolvidos permanece intocado. A entrada de encontro e a adaptação para efeitos também materializam as regras para cobrir novas entidades e owners de lado/global. Recursos de carteira não são confundidos com recursos do personagem pelo ID da entidade sintética.

`Set`, `Gain`, `Reset`, o reducer de mutações, regeneração e reembolsos respeitam o mesmo teto, inclusive quando o recurso permite exceder o máximo. Aumentar o máximo não libera o saldo. Um mínimo incompatível falha sem alteração parcial. Custos continuam usando as regras normais de disponibilidade/overdraft; o teto não concede recurso para pagá-los.

Rebind mantém o teto e a validação; JSON inválido não pode publicar uma regra abaixo do mínimo configurado. Armor e outras compensações continuam sendo influências na bucket pipeline, não regras dentro de `ResourcePool`.

Essas garantias se referem aos caminhos canônicos da engine, não a código externo que construa manualmente snapshots inconsistentes com `with { Current = ... }`. A engine normaliza o saldo na materialização; a certificação ampliada de migrações permanece na etapa 8.
