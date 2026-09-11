# SkogCli (F#)

A from-scratch, statically typed rewrite of [SkogCli](../README.md) — the
Python/Typer CLI in this repository — in F#. The Python implementation
remains at the repository root and is unaffected; this directory is a
self-contained parallel implementation, not a patch on top of it.

## Why

The Python CLI grew organically: settings are untyped `dict[str, Any]`,
several commands raise bare `RuntimeError`s when a directory setting hasn't
been configured (including one config lookup that is circular — it needs
the config file to find the config directory), and behaviour that should be
data (script metadata, agent config) is scattered across ad-hoc JSON files
with no shared schema. This rewrite keeps the same command surface and
underlying ideas (config management, a `basic-memory` wrapper, script
management, agent scripts) but backed by one small, explicit JSON value
type and modules with no circular dependencies.

## Layout

```
fsharp/
  src/
    SkogCli.Core/     JsonValue (a typed JSON tree), filesystem paths, Settings, process helper
    SkogCli.Memory/   thin wrapper around the external `basic-memory` CLI
    SkogCli.Scripts/  create/list/run/edit/search user scripts + JSON metadata
    SkogCli.Agent/    named agent configs, each backed by a generated ./scripts/<name>.sh
    SkogCli.Cli/      Argu-based argument parsing and command dispatch (the entry point)
  tests/
    SkogCli.Tests/    xUnit tests for Core.Json, Core.Settings and Scripts.Scripts
```

`SkogCli.Core` has no dependencies on the others; `Memory`, `Scripts` and
`Agent` each depend only on `Core`; `Cli` wires all four together. There are
no cross-module or circular dependencies.

### `JsonValue`

Every piece of dynamic configuration (settings, agent configs, script
metadata, chat history) is represented as:

```fsharp
type JsonValue =
    | JString of string
    | JNumber of float
    | JBool of bool
    | JNull
    | JArray of JsonValue list
    | JObject of Map<string, JsonValue>
```

with `getPath` / `setPath` / `removePath` / `flatten` operating on dotted
key paths (`"agent.coder.model"`), so callers pattern-match on a known
shape instead of hoping a `dict` key holds the type they expect.

## Building and testing

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
cd fsharp
dotnet build                                    # build everything
dotnet test tests/SkogCli.Tests                 # run the test suite
dotnet run --project src/SkogCli.Cli -- --help  # run the CLI
```

## Command surface

```
skogcli version

skogcli config show|list|get|set|reset|backup|restore|list-backups|chat-history|export-env
skogcli memory write|read|search|list|sync|status|bm
skogcli script list|run|create|edit|remove|info|code|copy|search|templates
skogcli agent list|create|set|get|read|send|edit-script|delete
```

Each subcommand supports `--help` for its exact flags (parsing is handled
by [Argu](https://fsprojects.github.io/Argu/), so usage text is always
in sync with the actual accepted flags).

### Configuration directories

Every directory SkogCli touches has an environment-variable override and a
sensible XDG-style default, so the CLI works with zero configuration:

| Purpose | Override | Default |
|---|---|---|
| config.json / credentials.json / backups | `SKOGAI_CONFIG_DIR` | `$XDG_CONFIG_HOME/skogcli` (`~/.config/skogcli`) |
| user scripts | `SKOGAI_SCRIPTS_DIR` | `$XDG_DATA_HOME/skogcli/scripts` (`~/.local/share/skogcli/scripts`) |
| global scripts | `SKOGAI_SCRIPTS_GLOBAL_DIR` | `/usr/local/share/skogcli/scripts` |
| script metadata | `SKOGAI_SCRIPT_METADATA_DIR` | `$XDG_DATA_HOME/skogcli/script_metadata.json` |
| script templates | `SKOGAI_TEMPLATES_DIR` or `script.templates_dir` setting | built-in `shell/basic` and `python/basic` templates |

Any config value can also be overridden per-invocation with an environment
variable, e.g. `SKOGAI_AGENT_DEFAULT_MODEL=gpt-5` overrides the
`agent.default_model` setting (`SKOGAI_TEST_*` takes precedence over that,
for use in tests).

## Deliberate differences from the Python CLI

These were dropped or changed on purpose rather than ported as-is:

- **No circular config-directory lookup.** The Python `get_config_dir()`
  reads a setting that itself requires loading the config file from the
  config directory. This only worked because `SKOGAI_CONFIG_DIR` happened
  to always be set; here directory resolution is non-recursive with a real
  default (see table above).
- **No bare `RuntimeError`s for unconfigured directories.** Every directory
  has a working default instead of demanding an environment variable be
  set first.
- **The `$`-prefixed "SkogAI notation" config shorthand and the
  `chat.history` → `settings.module.history` migration path** were dropped
  as historical, install-specific cruft rather than ported.
- **Script `create`/`copy` default to *not* opening `$EDITOR`** (`--edit`
  opts in), rather than always shelling out to an editor — friendlier for
  scripting and automation.
- **Python scripts run via `python3 <script> <args>`** rather than being
  dynamically imported as a module and having its `main()` called in-process;
  simpler and works for any Python script, not just ones following that
  convention.

## Not yet ported

The Python `script` command also has `batch`, `transform`, `generate`,
`import` and `import-file` subcommands, and `config` has `show-defaults` /
`edit-defaults` / `edit` (which open a whole JSON file in `$EDITOR`). These
were left out of this first pass as lower-value / more speculative features;
the architecture (`SkogCli.Scripts.Scripts`, `SkogCli.Core.Settings`) has
room to add them without restructuring anything.
