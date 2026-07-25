# Etch.TaskVerifier

CLI tool that parses and executes `<!-- verify: ... -->` directives from task files.

## Usage

```powershell
# Run all directives in a single task file
dotnet run --project tools/Etch.TaskVerifier -- task docs/01-foundations/FND-004.md

# Dry run - print what would run without executing
dotnet run --project tools/Etch.TaskVerifier -- task docs/01-foundations/FND-003.md --dry-run

# Run all tasks in a track
dotnet run --project tools/Etch.TaskVerifier -- track docs/01-foundations

# Run all tasks and emit JSON report
dotnet run --project tools/Etch.TaskVerifier -- all --json
```

## Supported Directives

| Verb | Description |
|------|-------------|
| `file-exists` | Checks that a file or directory exists |
| `aot-publish` | Runs `dotnet publish -p:PublishAot=true` and checks for warnings |
| `tunit` | Runs TUnit tests matching a class filter |
| `symbol-absent` | Checks that a pattern is NOT present in an assembly (uses Mono.Cecil) |
| `symbol-shape` | Checks type modifiers (sealed, abstract, public) |
| `trim-warning-count` | Counts trim/AOT warnings and compares to max |
| `bench-run` | Runs a benchmark and checks for success |
| `bench-alloc` | Runs a benchmark and checks allocation against budget |

## Directive Format

Directives in task files use HTML comment syntax:

```markdown
<!-- verify: aot-publish rid=win-x64 project=src/Etch.Abstractions/Etch.Abstractions.csproj -->
```

Arguments use `key=value` syntax with space separation. Values can be quoted:

```markdown
<!-- verify: symbol-shape assembly=src/Etch.Abstractions/bin/Debug/net10.0/Etch.Abstractions.dll type=Etch.Panic sealed=true -->
```

## Extending with New Checks

1. Create a new class extending `Check` in `Checks/`:
   ```csharp
   public sealed class MyCheck : Check
   {
       public override string Verb => "my-verb";
       public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
       {
           // implementation
       }
   }
   ```

2. Register in `CheckRegistry`:
   ```csharp
   static CheckRegistry()
   {
       // ... existing checks
       Register(new MyCheck());
   }
   ```

## JSON Output Schema

When `--json` is passed, output is a single JSON object:

```json
{
  "toolVersion": "1.0.0",
  "timestamp": "2026-04-23T12:00:00Z",
  "command": "task docs/01-foundations/FND-004.md",
  "totalChecks": 4,
  "passed": 3,
  "failed": 1,
  "skipped": 0,
  "errors": 0,
  "checks": [
    {
      "verb": "file-exists",
      "args": { "path": "build/aot-baseline-report.md" },
      "status": "Pass",
      "detail": "Path exists: /path/to/build/aot-baseline-report.md",
      "durationMs": 5,
      "taskFile": "docs/01-foundations/FND-004.md",
      "lineNumber": 42
    }
  ]
}
```
