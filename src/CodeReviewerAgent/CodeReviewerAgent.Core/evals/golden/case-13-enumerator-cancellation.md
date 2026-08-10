# case-13 — enumerator-cancellation

**Expectativa:** achado em `src/Chat/ChatStreamService.cs`
**Classe:** bug sutil de C# moderno · **Exige:** C# 8 (2019)
**Origem:** dogfood — moldado sobre `FlowingBot.Core/Services/ChatAskQuestionService.cs`, que **usa
o atributo corretamente**. O caso é a mesma forma com ele removido.

## O que o diff faz

Acrescenta um `async IAsyncEnumerable<string>` que recebe `CancellationToken cancellationToken = default`
e repassa o token às chamadas internas — mas **sem** `[EnumeratorCancellation]` no parâmetro.

## Por que é bug

Num iterador assíncrono, o consumidor cancela por `WithCancellation(token)`, que entrega o token ao
`GetAsyncEnumerator`. Sem `[EnumeratorCancellation]`, esse token não é ligado ao parâmetro do método:
ele fica **não consumido**, e o cancelamento pelo consumidor simplesmente não acontece.

O detalhe que torna o caso sutil é que o código *parece* correto: o token aparece na assinatura e é
repassado adiante. Quem chama passando o token diretamente como argumento é atendido. Quem cancela
pelo caminho idiomático — `await foreach (... .WithCancellation(token))` — não é, e o stream continua
até o fim.

## Honestidade: o compilador avisa

Verificado em .NET 10 — este é o único dos três casos sutis em que há aviso:

```
warning CS8425: Async-iterator has one or more parameters of type 'CancellationToken' but none of
them is decorated with the 'EnumeratorCancellation' attribute, so the cancellation token parameter
from the generated 'IAsyncEnumerable<>.GetAsyncEnumerator' will be unconsumed
```

Isso enfraquece um pouco o caso como medida de sutileza: um build limpo pegaria. Mantido mesmo assim,
porque é aviso e não erro — a classe de bug chega à produção o tempo todo por avisos ignorados — e
porque o modelo ainda precisa **conhecer o atributo** para apontá-lo. Se o resultado do E03 exigir um
caso sem rede de segurança do compilador, este é o primeiro a trocar.

## O que um bom achado diz

Nomeia `[EnumeratorCancellation]` e explica que sem ele o token de `WithCancellation` fica sem uso.
Sugerir apenas "trate o cancelamento" não é acionável — o código já parece tratar.
