# case-09 — collection-spread

**Expectativa:** `ExpectNoFinding` — isca em `return [.. configured, _options.FallbackAddress];`
**Classe:** armadilha de falso positivo · **Exige:** C# 12 (2023)

## Por que este código está correto

`[.. configured, _options.FallbackAddress]` é uma **collection expression** com elemento de spread.
O `..` desdobra `configured` dentro do novo array e acrescenta o endereço de fallback ao final. O
alvo é `string[]`, e o compilador materializa o array com o tamanho exato.

O método `Merge` logo abaixo usa dois spreads na mesma expressão, também válido.

## Como um modelo antigo erra

Não reconhece a sintaxe de coleção e lê o `..` como operador de intervalo mal formado, ou reclama que
o retorno não é um `string[]`. Alguns sugerem "usar `configured.Append(...).ToArray()`", que é
exatamente a forma que a collection expression substitui.

## Verificação de honestidade

Compilado e executado em .NET 10, **sem aviso**:

```
a,b          // Recipients.Build(["a"], "b")
```

## O que a skill diz hoje sobre isto

A skill `csharp` cita *"collection initializers"*, que é termo do C# 3 (`new List<int> { 1, 2, 3 }`),
**não** collection expression. A rigor a skill não cobre esta sintaxe — mas a proximidade do
vocabulário é maior aqui do que no caso 08. Se o delta baseline↔harness for grande neste caso e
pequeno no 08, essa diferença de vocabulário é a primeira hipótese a investigar.

## O que conta como queda

Só achado na linha da isca. O `if` de guarda logo acima é código correto e cabe comentário legítimo
sobre ele; não conta.
