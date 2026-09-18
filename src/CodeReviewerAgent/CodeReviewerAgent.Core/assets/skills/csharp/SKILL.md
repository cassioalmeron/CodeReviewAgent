---
name: csharp
description: C# coding conventions enforced in this codebase — naming, brace-less single-statement blocks, early return, expression-bodied members, string interpolation, async/await and modern C#. Use when the diff changes C# code (.cs files), to report convention violations.
metadata:
  applies-to: "*.cs"
---
# C# conventions

- PascalCase for types, methods, and properties; camelCase for locals and parameters; private fields prefixed with `_`.
- Omit braces on single-statement blocks (`if`, `else`, `for`, `foreach`, `while`, `using`). A single-line body must not be wrapped in `{ }`.
- After an `if` block that returns, drop the `else` and use early return.
- Use expression-bodied members (`=>`) for members that are a single expression.
- Use string interpolation (`$"..."`); never concatenate with `+` or use `string.Format`.
- Use `async`/`await` for I/O; suffix async methods with `Async`; never `.Result` or `.Wait()`.
- Prefer modern C#: records, pattern matching, `switch` expressions, collection initializers.
- Nullable reference types enabled; use `?` and handle nulls explicitly.

Single-statement `if` — omit the braces:

```csharp
// Wrong
if (user == null)
{
    return NotFound();
}

// Right
if (user == null)
    return NotFound();
```
