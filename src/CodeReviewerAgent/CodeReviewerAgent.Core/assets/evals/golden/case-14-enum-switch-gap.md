# case-14 — enum-switch-gap

**Expectativa:** achado em `Backend/RewardStar.Api/Controllers/GameController.cs`
**Classe:** bug que só se enxerga com contexto · **Exige:** nada (agnóstico de versão)
**Origem:** dogfood — `RewardStar`. O enum `RewardType` e o dicionário `REWARD_COSTS` do
`GameController` existem no repositório real, com estes mesmos três valores.

## Os dois pontos, ambos em linhas adicionadas

**Ponto 1** — `Models/RewardType.cs` ganha um valor:

```csharp
+    Chocolate = 4
```

**Ponto 2** — `Controllers/GameController.cs` ganha um endpoint que indexa o dicionário de custos:

```csharp
+        var cost = REWARD_COSTS[reward];
```

## Por que só se vê junto

Cada trecho é irrepreensível sozinho. Acrescentar valor a um enum é rotina. Indexar um dicionário de
custos por tipo de recompensa é o padrão que já existe no arquivo.

O defeito é a **relação**: `REWARD_COSTS` (linha de contexto, não modificada) mapeia apenas MMs, Bibs
e Caramel. O endpoint novo aceita `RewardType` do corpo da requisição, então `Chocolate` chega e
`REWARD_COSTS[reward]` lança `KeyNotFoundException` — HTTP 500 numa rota pública, acionável por
qualquer cliente que passe `4`.

Nenhum aviso de compilador: indexador de dicionário não tem verificação de exaustividade. Uso de
`switch` expression daria CS8509; o dicionário não dá nada. Foi escolhido de propósito, e é a forma
que o repositório real usa.

## O que um bom achado diz

Liga os dois trechos: o valor novo do enum não tem entrada no mapa que o endpoint novo consulta.
Correção: acrescentar `Chocolate` a `REWARD_COSTS`, ou usar `TryGetValue` e responder 400.

## Risco de atribuição, conhecido

`ExpectFinding` aponta **um** arquivo, e este caso tem dois candidatos legítimos. Um achado dizendo
"adicionar `Chocolate` quebra `REWARD_COSTS`" apontando `RewardType.cs` está **certo** e ainda assim
conta como falha aqui.

A expectativa aponta o controller porque é onde o defeito mora e onde a correção acontece — o enum
ganhou um valor válido, quem está errado é o mapa incompleto consultado pelo código novo. Se a medição
mostrar o modelo citando consistentemente o arquivo do enum, o `MissDetail` vai registrar, e aí a
decisão a rever é a expectativa de arquivo único, não o caso.
