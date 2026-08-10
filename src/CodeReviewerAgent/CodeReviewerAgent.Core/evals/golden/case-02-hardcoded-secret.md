# case-02 — hardcoded-secret

**Expectativa:** achado em `src/Services/PaymentClient.cs`
**Classe:** bug de manual — piso de comparação, não discrimina modelo

## O que o diff faz

Atribui uma chave de API literal no construtor:

```csharp
_apiKey = "sk_live_51H8xQh2eZvKYlo3kProductionSecret";
```

## Por que é bug

Segredo em código-fonte vaza para o histórico do git e para qualquer clone do repositório. O prefixo
`sk_live_` marca credencial de produção, o que agrava: rotacionar exige reemitir a chave, e apagar o
commit não basta, porque o valor já esteve publicado.

## O que um bom achado diz

Que o valor sai do código para variável de ambiente ou cofre de segredos, e que a chave exposta
precisa ser **rotacionada** — remover a linha não desfaz o vazamento. Achado que só manda "não
hardcode" resolve metade.

## Por que este caso não discrimina

O formato do literal entrega o problema sem exigir raciocínio. É piso.
