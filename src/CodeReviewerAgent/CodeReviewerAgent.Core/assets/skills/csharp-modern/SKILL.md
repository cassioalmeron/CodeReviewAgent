---
name: csharp-modern
description: The C# language baseline of this codebase — it targets .NET 10, so every syntax through C# 14 is available and valid. Use when the diff changes C# code (.cs files), to avoid reporting unfamiliar but current language features as errors.
metadata:
  applies-to: "*.cs"
---
# C# language baseline

**Hard rule: never report that C# code is invalid, malformed, or will not compile.**

This code already builds — a syntax error would have failed the build long before this review. You
are reading a diff, not compiling it. A "this will not compile" finding is therefore wrong by
construction, and it is the most expensive kind of wrong: it sends the author to check something
that was never broken.

This codebase targets **.NET 10**, so every feature through **C# 14** is in play. The language gained
new declaration forms in C# 11, 12, 13 and 14. If a construct looks unfamiliar, that is evidence
about your training data, not about the code.

## What "unfamiliar but valid" looks like

Every one of these compiles today. If they look wrong to you, that is the exact reflex this rule
exists to stop — and the constructs you meet in the diff will be others just like them:

```csharp
// C# 14 — extension members: an 'extension(receiver)' block inside a static class.
// The receiver is declared once, in the block header, and every member inside extends it.
// No 'this' modifier, no 'for' keyword — the parenthesised receiver is the whole syntax.
public static class TextExtensions
{
    extension(string text)
    {
        public bool IsBlank => string.IsNullOrWhiteSpace(text);

        public string Truncate(int max) => text.Length <= max ? text : text[..max];
    }
}

// C# 14 — 'field' is the compiler-generated backing field of this property
public string Name
{
    get => field;
    set => field = value?.Trim() ?? string.Empty;
}

// C# 13 — params over any collection type, not just arrays
public void Log(params ReadOnlySpan<string> lines) { }

// C# 12 — an alias for any type, tuples included
using Point = (int X, int Y);

// C# 11 — list pattern: first element 1, last 5, anything between
if (values is [1, .., 5])
    return true;
```

None of that is a typo, a draft, or a mistake. It is current C#.

## Do not route around the rule

The rule covers the claim, not the wording. All of these are the same forbidden finding:

- "this is not valid C# / will not compile / is malformed"
- "this keyword does not exist / this identifier is undeclared"
- "this should be rewritten as a static method with a `this` parameter"
- "this does not follow the usual pattern for declaring this kind of member"
- "for consistency, declare it the conventional way"

If your objection to a construct is that you do not recognize the form it is declared in, you have no
finding. Say nothing and move on.

## What is still fair game

Behaviour. Modern syntax hides real defects — surprising lifetimes, shared mutable state, value-type
copies, attributes that claim more than the code delivers. Report what the code *does*.

"This shares one counter across every call" is a finding. "This is not valid C#" is not.
