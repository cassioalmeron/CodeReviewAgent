# case-06 — csharp-conventions

**Expectativa:** achado em `src/Services/NotificationService.cs`
**Classe:** convenção de projeto — mede o harness de skills, não capacidade do modelo

## O que o diff faz

Acrescenta um método `Notify` que viola três regras da skill `csharp`:

```csharp
if (user == null)
{
    return false;          // 1. chaves em bloco de uma instrução
}

var subject = "Notification for " + user.Name + " (" + user.Email + ")";   // 2. concatenação
```

E, 3, não usa early return no corpo restante.

## Por que conta como achado

Nada aqui está errado em C#: o código compila e funciona. O que se mede é se o agente aplica as
**diretrizes deste projeto**, que só chegam até ele pela skill `csharp`. Sem a skill carregada, o
achado esperado não tem como aparecer — e é justamente isso que torna o caso útil para comparar as
condições baseline e harness.

## O que um bom achado diz

Cita a regra violada e a linha. Sugerir "melhorar a legibilidade" sem apontar a convenção não é
acionável e não deveria pontuar.

## Ressalva conhecida

O caso mistura três violações no mesmo diff. Para medir detecção funciona — basta uma keyword casar
— mas dá crédito parcial difícil de interpretar quando a US-013 for comparar modelos. Se aparecer
resultado ambíguo, considerar separar em casos distintos.
