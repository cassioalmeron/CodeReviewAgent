# case-01 — sql-injection

**Expectativa:** achado em `src/Data/UserRepository.cs`
**Classe:** bug de manual — piso de comparação, não discrimina modelo

## O que o diff faz

Troca uma consulta LINQ em memória por SQL montado com concatenação de string:

```csharp
var sql = "SELECT * FROM Users WHERE Name = '" + name + "'";
```

## Por que é bug

`name` vem de fora e entra literalmente no comando. Um valor como `' OR '1'='1` muda a semântica da
consulta; um com `;` encadeia outro comando. É injeção de SQL clássica, CWE-89.

## O que um bom achado diz

Aponta a concatenação como a causa e propõe consulta parametrizada — parâmetro nomeado, não escape
manual de aspas. Escapar à mão é remendo, e é a sugestão que separa achado bom de achado decorado.

## Por que este caso não discrimina

Qualquer modelo que já viu código encontra isto. Serve de piso: se um modelo erra aqui, o resultado
dos casos difíceis nem precisa ser lido.
