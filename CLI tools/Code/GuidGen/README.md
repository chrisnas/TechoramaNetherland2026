# GuidGen

A .NET CLI tool to generate GUIDs in various formats.

## Installation

```bash
dotnet tool install --global dotnet-guidgen
```

## Usage

```bash
# Default: one hyphenated GUID
guidgen

# Three uppercase digit-only GUIDs
guidgen --format N --upper --count 3
```

## Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--format` | `-f` | GUID format: N (digits), D (hyphens), B (braces), P (parentheses), X (hex) | D |
| `--upper` | `-u` | Output in uppercase | false |
| `--count` | `-c` | Number of GUIDs to generate | 1 |

## License

MIT
