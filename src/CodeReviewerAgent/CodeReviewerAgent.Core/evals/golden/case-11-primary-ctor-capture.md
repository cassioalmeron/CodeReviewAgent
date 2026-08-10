# case-11 — primary-ctor-capture

**Expectativa:** achado em `src/Resilience/RetryPolicy.cs`
**Classe:** bug sutil de C# moderno · **Exige:** C# 12 (2023)

## O que o diff faz

Troca um método que recebia `attempts` por parâmetro por uma classe com **primary constructor**:

```csharp
public class RetryPolicy(int attempts)
{
    public Func<bool> ShouldRetry() => () => attempts-- > 0;
    public bool IsExhausted => attempts <= 0;
}
```

## Por que é bug

Parâmetro de primary constructor capturado por um lambda vira **campo de apoio da instância**. Antes,
`attempts` era parâmetro do método: cada chamada tinha o seu contador. Depois, existe **um contador
por instância**, compartilhado por todos os predicados que ela entregar.

Comportamento verificado em .NET 10, com `new RetryPolicy(2)`:

```
first():True  first():True  second():False
```

O segundo predicado nasce já esgotado, porque o primeiro consumiu as duas tentativas. Quem chama
`ShouldRetry()` duas vezes espera duas políticas independentes e recebe duas visões do mesmo contador.
`IsExhausted` observa o mesmo campo, então também muda sob os pés de quem consulta.

Não há aviso do compilador. O código é válido e a mudança parece uma simplificação.

## O que um bom achado diz

Que o parâmetro capturado passa a ser estado da instância, compartilhado entre as invocações, e que
a versão anterior era correta por acidente de escopo. Correção: capturar uma cópia local
(`var remaining = attempts;`) dentro de `ShouldRetry`.

## Cuidado de medição

Keywords deliberadamente estreitas: `captur`, `backing field`, `shared state`, `shared across`.
**Não** usar `constructor` — qualquer comentário genérico sobre o construtor passaria, que é o risco
que a US levanta nominalmente sobre este caso.
