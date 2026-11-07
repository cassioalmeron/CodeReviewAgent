# large-06 — timeout-unit

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/LlmClientFactory.cs`
**Classe:** bug de unidade · **Tamanho:** 482 linhas alteradas em 9 arquivos
**Origem:** commit `6c34f0ee`, "HttpResilience with Decorator.", com 1 linha trocada

## O que o diff faz

Troca a resiliência de HTTP por um decorador: o transporte simples faz a chamada, o resiliente
envolve ele com retry. A fábrica passa a montar os dois juntos para cada cliente.

## O defeito plantado

O tempo limite do cliente Ollama passou de segundos para milissegundos:

```
var http = new HttpClient { BaseAddress = new Uri(host), Timeout = TimeSpan.FromMilliseconds(120) };
```

## Por que é problema

120 milissegundos não dão para nenhuma chamada de modelo. Toda requisição estoura o prazo, o retry
tenta de novo 3 vezes com espera exponencial, e a chamada termina em falha de tempo esgotado. O
cliente fica inutilizável, e o erro aparece como problema de rede em vez de erro de configuração.

O valor certo está no mesmo arquivo, dois métodos acima: `TimeSpan.FromSeconds(120)`. A evidência
está dentro do diff.

## O que um bom achado diz

Que a unidade está errada, e que 120 milissegundos é curto demais para uma chamada de modelo, com o
contraste do outro método. Severidade alta se justifica porque o componente simplesmente não
funciona.

## Cuidado de medição

Nenhum teste do diff monta clientes pela fábrica, então nada falha por causa do plantio.
