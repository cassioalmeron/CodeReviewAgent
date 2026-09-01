# case-04 — off-by-one

**Expectativa:** achado em `src/Utils/ArrayHelper.cs`
**Classe:** bug de manual — piso de comparação, não discrimina modelo

## O que o diff faz

Troca um `foreach` correto por um `for` com comparação errada:

```csharp
for (var i = 0; i <= values.Length; i++)
    total += values[i];
```

## Por que é bug

`<=` faz a última iteração acessar `values[values.Length]`, um índice além do fim.
`IndexOutOfRangeException` em toda chamada com array não vazio — o método nunca funciona.

Vale notar que a versão anterior (`foreach`) era correta e mais simples: o diff não introduz só um
defeito, introduz uma regressão desnecessária.

## O que um bom achado diz

Identifica o `<=` como a causa e propõe `<`. Achado que fala em "possível problema de limite" sem
apontar o operador é vago demais para agir.

## Por que este caso não discrimina

Off-by-one com `<=` é o exemplo canônico do gênero. É piso.
