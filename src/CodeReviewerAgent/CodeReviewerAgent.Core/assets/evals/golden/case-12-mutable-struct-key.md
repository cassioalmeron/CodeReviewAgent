# case-12 — mutable-struct-key

**Expectativa:** achado em `src/Caching/VersionCache.cs`
**Classe:** bug sutil de C# moderno · **Exige:** C# 10 (2021)

## O que o diff faz

Declara `public record struct CacheKey(string Tenant, int Version);` e usa como chave de dicionário,
com um método que grava e depois incrementa a versão da chave:

```csharp
_entries[key] = payload;
key.Version++;
return _entries.TryGetValue(key, out var renewed) ? renewed : null;
```

## Por que é bug

Dois fatos do C# moderno se combinam. Primeiro, **`record struct` posicional é mutável**: as
propriedades geradas têm `set`, ao contrário do `record class`, onde são `init`. Segundo, struct é
tipo de valor: `key` é uma cópia local, e incrementá-la não toca a chave já gravada no dicionário.

Depois do `++`, `key` tem hash code diferente do que foi usado na inserção. O `TryGetValue` procura
um par que não existe.

Comportamento verificado em .NET 10:

```
lookup após mutação: False
entrada original ainda lá: True
```

Ou seja: `Renew` devolve `null` **sempre**, e a entrada gravada continua no dicionário, agora
inalcançável por quem só tem a chave mutada. Vazamento silencioso, não exceção.

## O que um bom achado diz

Aponta a mutação da chave depois da inserção e que `record struct` posicional é mutável — não basta
dizer "cuidado com structs". Correção: declarar `readonly record struct`, o que faz o `key.Version++`
virar erro de compilação, ou construir uma chave nova em vez de mutar.

## Cuidado de medição

Keywords: `mutab`, `hash code`, `unreachable`, `copy of the struct`, `always null`. Todas exigem
nomear o mecanismo. Um "considere usar uma chave imutável" sem explicação não pontua, e não deveria.
