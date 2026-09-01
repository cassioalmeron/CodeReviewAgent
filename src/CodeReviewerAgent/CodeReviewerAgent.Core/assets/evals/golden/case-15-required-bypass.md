# case-15 — required-bypass

**Expectativa:** achado em `src/Notifications/Recipient.cs`
**Classe:** bug que só se enxerga com contexto · **Exige:** C# 11 (2022)

## Os dois pontos, ambos em linhas adicionadas

**Ponto 1** — o record ganha um membro obrigatório:

```csharp
+    public required int RetryLimit { get; init; }
```

**Ponto 2** — mais abaixo no mesmo arquivo, um construtor de conveniência:

```csharp
+    [SetsRequiredMembers]
+    public Recipient(string name) => Name = name;
```

## Por que só se vê junto

Sozinho, o ponto 1 é exatamente o que `required` existe para fazer: obrigar quem constrói a informar
o valor. Sozinho, o ponto 2 é um atalho comum — `[SetsRequiredMembers]` diz ao compilador "este
construtor cuida de todos os membros obrigatórios", dispensando o inicializador de objeto.

Juntos, o ponto 2 **mente**. Ele afirma cobrir todos os membros obrigatórios e só atribui `Name`.
`new Recipient("Ana")` compila e produz um objeto com `RetryLimit = 0`, apesar de o membro ser
`required`. A garantia que o ponto 1 estabeleceu foi desligada pelo ponto 2, no mesmo diff.

## Honestidade: nenhum aviso, e isso foi escolhido

Verificado em .NET 10. Com membro obrigatório de **tipo referência** o compilador ainda salva o
revisor com CS8618 ("must contain a non-null value when exiting constructor"). Com **tipo valor**,
como o `int` daqui, não há aviso nenhum: `0` é valor válido e a análise de nulidade não tem o que
dizer.

```
case-15 RetryLimit silently = 0
```

A escolha do `int` é deliberada. Com `string` o caso mediria o compilador; com `int` mede o revisor.

## O que um bom achado diz

Nomeia `[SetsRequiredMembers]` e diz que o construtor não atribui `RetryLimit`, então a
obrigatoriedade é contornada em silêncio. Correção: atribuir no construtor, ou remover o atributo e
deixar o inicializador de objeto obrigatório de volta.

## Cuidado de medição

Keywords: `retrylimit`, `setsrequiredmembers`, `never set`. A primeira só aparece se o modelo tiver
lido o ponto 1 — é ela que separa "entendeu a relação" de "comentou sobre o construtor".
