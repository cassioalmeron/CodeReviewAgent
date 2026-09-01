# case-03 — null-dereference

**Expectativa:** achado em `src/Services/OrderService.cs`
**Classe:** bug de manual — piso de comparação, não discrimina modelo

## O que o diff faz

```csharp
var customer = _customers.Find(order.CustomerId);
return customer.Email.ToUpperInvariant();
```

## Por que é bug

`Find` devolve `null` quando não acha, e o retorno é desreferenciado sem verificação. Pedido com
`CustomerId` órfão — cliente removido, id inválido — derruba com `NullReferenceException`. Há um
segundo nível: mesmo com `customer` não nulo, `Email` pode ser nulo e `ToUpperInvariant()` estoura
igual.

## O que um bom achado diz

Aponta a ausência de verificação após `Find` e propõe caminho explícito para o caso não encontrado.
Achado melhor ainda nota que a cadeia tem dois pontos de risco, não um.

## Por que este caso não discrimina

`Find` seguido de acesso direto é padrão reconhecível. É piso.
