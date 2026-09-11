/// Argu argument definitions for every SkogCli subcommand.
///
/// Convention used throughout: a case with no fields is a boolean flag; a case
/// named `Args` marked `[<MainCommand>]` collects the command's positional
/// tokens as a plain string list, which handlers then destructure by
/// position. This keeps the type definitions small while Argu still owns
/// flag parsing, `--help` generation and error messages.
module SkogCli.Cli.Args

open Argu

/// Argu treats a zero-field union case as a boolean flag rather than a
/// subcommand once its union also contains subcommand-shaped cases (ones
/// carrying `ParseResults<_>`). Wrapping such leaves in `ParseResults<NoArgs>`
/// keeps them selectable as ordinary subcommands, e.g. `skogcli config backup`.
[<RequireQualifiedAccess>]
type NoArgs =
    | [<Hidden>] Unused

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Unused -> ""

// ---------------------------------------------------------------- config ---

[<RequireQualifiedAccess>]
type ConfigGetArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-r")>] Raw
    | [<AltCommandLine("-j")>] Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "configuration key, e.g. memory.page_size"
            | Raw -> "output the raw value without formatting"
            | Json -> "output as formatted JSON"

[<RequireQualifiedAccess>]
type ConfigSetArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "<key> <value>"

[<RequireQualifiedAccess>]
type ConfigListArgs =
    | [<AltCommandLine("--env-only")>] EnvOnly

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | EnvOnly -> "show only environment-variable keys (*.env.*)"

[<RequireQualifiedAccess>]
type ConfigResetArgs =
    | [<AltCommandLine("-y")>] Yes

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Yes -> "reset without prompting for confirmation"

[<RequireQualifiedAccess>]
type ConfigRestoreArgs =
    | [<MainCommand>] Args of string list
    | [<AltCommandLine("-y")>] Yes

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "backup file path (defaults to the most recent backup)"
            | Yes -> "restore without prompting for confirmation"

[<RequireQualifiedAccess>]
type ConfigChatHistoryArgs =
    | Clear
    | [<AltCommandLine("-n")>] Limit of count: int
    | [<AltCommandLine("-j")>] Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clear -> "clear all chat history"
            | Limit _ -> "number of history items to show (default 10)"
            | Json -> "output in JSON format"

[<RequireQualifiedAccess>]
type ConfigExportEnvArgs =
    | [<AltCommandLine("-n")>] Namespace of ns: string
    | [<AltCommandLine("-o")>] Output of path: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Namespace _ -> "only export variables from this namespace (comma-separated for several)"
            | Output _ -> "write to a file instead of stdout"

[<RequireQualifiedAccess>]
type ConfigCommand =
    | [<CliPrefix(CliPrefix.None)>] Show of ParseResults<NoArgs>
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ConfigListArgs>
    | [<CliPrefix(CliPrefix.None)>] Get of ParseResults<ConfigGetArgs>
    | [<CliPrefix(CliPrefix.None)>] Set of ParseResults<ConfigSetArgs>
    | [<CliPrefix(CliPrefix.None)>] Reset of ParseResults<ConfigResetArgs>
    | [<CliPrefix(CliPrefix.None)>] Backup of ParseResults<NoArgs>
    | [<CliPrefix(CliPrefix.None); CustomCommandLine("restore")>] Restore of ParseResults<ConfigRestoreArgs>
    | [<CliPrefix(CliPrefix.None); CustomCommandLine("list-backups")>] List_Backups of ParseResults<NoArgs>
    | [<CliPrefix(CliPrefix.None); CustomCommandLine("chat-history")>] Chat_History of ParseResults<ConfigChatHistoryArgs>
    | [<CliPrefix(CliPrefix.None); CustomCommandLine("export-env")>] Export_Env of ParseResults<ConfigExportEnvArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Show _ -> "display the current configuration"
            | List _ -> "list all configuration keys and values"
            | Get _ -> "get the value of a configuration key"
            | Set _ -> "set the value of a configuration key"
            | Reset _ -> "reset configuration to defaults"
            | Backup _ -> "create a backup of the configuration files"
            | Restore _ -> "restore configuration from a backup"
            | List_Backups _ -> "list available configuration backups"
            | Chat_History _ -> "view or clear interaction history"
            | Export_Env _ -> "print `export NAME=value` lines for *.env.* config keys"

// ---------------------------------------------------------------- memory ---

[<RequireQualifiedAccess>]
type MemoryWriteArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-c")>] Content of text: string
    | [<CustomCommandLine("--tags"); AltCommandLine("-t")>] Tag of tags: string
    | [<AltCommandLine("-p")>] Project of name: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "<title> <folder>"
            | Content _ -> "note content (if omitted, read from stdin)"
            | Tag _ -> "comma-separated tags"
            | Project _ -> "specific basic-memory project to use"

[<RequireQualifiedAccess>]
type MemoryReadArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-p")>] Page of n: int
    | [<AltCommandLine("-s")>] Page_Size of n: int
    | [<AltCommandLine("-P")>] Project of name: string
    | [<AltCommandLine("-r")>] Raw

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "note identifier ('folder/title' or 'latest')"
            | Page _ -> "page number for pagination (default 1)"
            | Page_Size _ -> "items per page (default 10)"
            | Project _ -> "specific basic-memory project to use"
            | Raw -> "display raw markdown without formatting"

[<RequireQualifiedAccess>]
type MemorySearchArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | Permalink
    | Title
    | [<AltCommandLine("-a")>] After_Date of date: string
    | [<AltCommandLine("-b")>] Before_Date of date: string
    | [<AltCommandLine("-p")>] Page of n: int
    | [<AltCommandLine("-s")>] Page_Size of n: int
    | [<AltCommandLine("-P")>] Project of name: string
    | [<AltCommandLine("-f")>] Format of fmt: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "search query"
            | Permalink -> "search only in permalink values"
            | Title -> "search only in title values"
            | After_Date _ -> "filter results after this date"
            | Before_Date _ -> "filter results before this date"
            | Page _ -> "page number for pagination (default 1)"
            | Page_Size _ -> "items per page (default 10)"
            | Project _ -> "specific basic-memory project to search"
            | Format _ -> "output format: table, json, markdown"

[<RequireQualifiedAccess>]
type MemoryListArgs =
    | [<AltCommandLine("-t")>] Type of activityType: string
    | [<AltCommandLine("-f")>] Folder of folder: string
    | [<AltCommandLine("-d")>] Depth of n: int
    | [<AltCommandLine("-T")>] Timeframe of tf: string
    | [<AltCommandLine("-p")>] Page of n: int
    | [<AltCommandLine("-s")>] Page_Size of n: int
    | [<AltCommandLine("-m")>] Max_Related of n: int
    | [<AltCommandLine("-P")>] Project of name: string
    | Format of fmt: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Type _ -> "filter by activity type"
            | Folder _ -> "filter by folder"
            | Depth _ -> "depth of related entities to show (default 1)"
            | Timeframe _ -> "time range, e.g. 7d, 2w, 1m (default 7d)"
            | Page _ -> "page number for pagination (default 1)"
            | Page_Size _ -> "items per page (default 10)"
            | Max_Related _ -> "max related items per result (default 5)"
            | Project _ -> "specific basic-memory project to list from"
            | Format _ -> "output format: table, json, markdown"

[<RequireQualifiedAccess>]
type MemorySyncArgs =
    | [<AltCommandLine("-p")>] Project of name: string
    | [<AltCommandLine("-f")>] Force
    | [<AltCommandLine("-n")>] Dry_Run
    | [<AltCommandLine("-v")>] Verbose

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Project _ -> "specific basic-memory project to sync"
            | Force -> "force sync even if no changes detected"
            | Dry_Run -> "show what would be synced without making changes"
            | Verbose -> "show detailed output"

[<RequireQualifiedAccess>]
type MemoryStatusArgs =
    | [<AltCommandLine("-p")>] Project of name: string
    | [<AltCommandLine("-f")>] Format of fmt: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Project _ -> "show status for a specific project"
            | Format _ -> "output format: table, json, yaml"

[<RequireQualifiedAccess>]
type MemoryBmArgs =
    | [<MainCommand>] Args of string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "arguments to pass straight through to basic-memory"

[<RequireQualifiedAccess>]
type MemoryCommand =
    | [<CliPrefix(CliPrefix.None)>] Write of ParseResults<MemoryWriteArgs>
    | [<CliPrefix(CliPrefix.None)>] Read of ParseResults<MemoryReadArgs>
    | [<CliPrefix(CliPrefix.None)>] Search of ParseResults<MemorySearchArgs>
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<MemoryListArgs>
    | [<CliPrefix(CliPrefix.None)>] Sync of ParseResults<MemorySyncArgs>
    | [<CliPrefix(CliPrefix.None)>] Status of ParseResults<MemoryStatusArgs>
    | [<CliPrefix(CliPrefix.None)>] Bm of ParseResults<MemoryBmArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Write _ -> "create or update a note"
            | Read _ -> "read a note by its identifier"
            | Search _ -> "search notes by content or metadata"
            | List _ -> "list recent notes and activity"
            | Sync _ -> "synchronize knowledge files with the database"
            | Status _ -> "show project information and sync status"
            | Bm _ -> "direct passthrough to the basic-memory command"

// ---------------------------------------------------------------- script ---

[<RequireQualifiedAccess>]
type ScriptListArgs =
    | No_Global
    | [<AltCommandLine("-m")>] Metadata

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | No_Global -> "exclude global scripts"
            | Metadata -> "show script metadata in a table"

[<RequireQualifiedAccess>]
type ScriptRunArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | No_Global

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "<name> [args...]"
            | No_Global -> "don't look for the script among global scripts"

[<RequireQualifiedAccess>]
type ScriptCreateArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-t")>] Type of scriptType: string
    | Template of name: string
    | [<AltCommandLine("-g")>] Global
    | [<AltCommandLine("-d")>] Description of text: string
    | Edit

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name for the new script"
            | Type _ -> "script type: shell or python (default shell)"
            | Template _ -> "template to use (default basic)"
            | Global -> "install as a global script"
            | Description _ -> "description of the script"
            | Edit -> "open $EDITOR after creating the script"

[<RequireQualifiedAccess>]
type ScriptEditArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | No_Global

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the script to edit"
            | No_Global -> "don't look for the script among global scripts"

[<RequireQualifiedAccess>]
type ScriptRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | No_Global
    | [<AltCommandLine("-f")>] Force

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the script to remove"
            | No_Global -> "don't look for the script among global scripts"
            | Force -> "remove without confirmation"

[<RequireQualifiedAccess>]
type ScriptInfoArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | No_Global

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the script"
            | No_Global -> "don't look for the script among global scripts"

[<RequireQualifiedAccess>]
type ScriptCodeArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-c")>] Content of text: string
    | [<AltCommandLine("-f")>] File of path: string
    | [<AltCommandLine("-o")>] Output of path: string
    | No_Global

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the script"
            | Content _ -> "new content for the script"
            | File _ -> "read new content from this file"
            | Output _ -> "write the current content here instead of printing it"
            | No_Global -> "don't look for the script among global scripts"

[<RequireQualifiedAccess>]
type ScriptCopyArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | No_Global_Source
    | [<AltCommandLine("-g")>] Global_Dest

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "<source> <destination>"
            | No_Global_Source -> "only look for the source script among user scripts"
            | Global_Dest -> "create the copy as a global script"

[<RequireQualifiedAccess>]
type ScriptSearchArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-r")>] Regex
    | [<AltCommandLine("-c")>] Case_Sensitive
    | No_Global
    | [<AltCommandLine("-o")>] Output of path: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "text or regular expression to search for"
            | Regex -> "treat the pattern as a regular expression"
            | Case_Sensitive -> "make the search case-sensitive"
            | No_Global -> "exclude global scripts"
            | Output _ -> "write results to this file"

[<RequireQualifiedAccess>]
type ScriptCommand =
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ScriptListArgs>
    | [<CliPrefix(CliPrefix.None)>] Run of ParseResults<ScriptRunArgs>
    | [<CliPrefix(CliPrefix.None)>] Create of ParseResults<ScriptCreateArgs>
    | [<CliPrefix(CliPrefix.None)>] Edit of ParseResults<ScriptEditArgs>
    | [<CliPrefix(CliPrefix.None)>] Remove of ParseResults<ScriptRemoveArgs>
    | [<CliPrefix(CliPrefix.None)>] Info of ParseResults<ScriptInfoArgs>
    | [<CliPrefix(CliPrefix.None)>] Code of ParseResults<ScriptCodeArgs>
    | [<CliPrefix(CliPrefix.None)>] Copy of ParseResults<ScriptCopyArgs>
    | [<CliPrefix(CliPrefix.None)>] Search of ParseResults<ScriptSearchArgs>
    | [<CliPrefix(CliPrefix.None)>] Templates of ParseResults<NoArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | List _ -> "list all available custom scripts"
            | Run _ -> "run a custom script"
            | Create _ -> "create a new custom script from a template"
            | Edit _ -> "open an existing script in $EDITOR"
            | Remove _ -> "remove a custom script"
            | Info _ -> "show detailed information about a script"
            | Code _ -> "view or update script content without an editor"
            | Copy _ -> "copy a script to create a new one"
            | Search _ -> "search for text across scripts"
            | Templates _ -> "list available script templates"

// ----------------------------------------------------------------- agent ---

[<RequireQualifiedAccess>]
type AgentCreateArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-m")>] Model of model: string
    | [<AltCommandLine("-s")>] System of prompt: string
    | [<AltCommandLine("-d")>] Description of text: string
    | [<AltCommandLine("-c")>] Command of template: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name for the new agent"
            | Model _ -> "model to use for this agent"
            | System _ -> "system prompt for this agent"
            | Description _ -> "description of this agent"
            | Command _ -> "command template, e.g. 'mytool run --text {message}'"

[<RequireQualifiedAccess>]
type AgentSetArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-a")>] Agent of name: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "<key> <value>"
            | Agent _ -> "agent name (if not included in the key)"

[<RequireQualifiedAccess>]
type AgentGetArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-a")>] Agent of name: string
    | [<AltCommandLine("-r")>] Raw
    | [<AltCommandLine("-j")>] Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "configuration key"
            | Agent _ -> "agent name (if not included in the key)"
            | Raw -> "output the raw value without formatting"
            | Json -> "output as formatted JSON"

[<RequireQualifiedAccess>]
type AgentReadArgs =
    | [<MainCommand>] Args of string list
    | [<AltCommandLine("-a")>] Agent of name: string
    | [<AltCommandLine("-r")>] Raw
    | [<AltCommandLine("-j")>] Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the agent to read"
            | Agent _ -> "name of the agent to read (alternative to the positional name)"
            | Raw -> "display raw response without formatting"
            | Json -> "output response in JSON format"

[<RequireQualifiedAccess>]
type AgentSendArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-a")>] Agent of name: string
    | [<AltCommandLine("-m")>] Model of model: string
    | [<AltCommandLine("-r")>] Raw
    | [<AltCommandLine("-j")>] Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "message to send to the agent"
            | Agent _ -> "name of the agent to send the message to (default: default)"
            | Model _ -> "model to use for this interaction"
            | Raw -> "display raw response without formatting"
            | Json -> "output response in JSON format"

[<RequireQualifiedAccess>]
type AgentEditScriptArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the agent whose script to edit"

[<RequireQualifiedAccess>]
type AgentDeleteArgs =
    | [<MainCommand; ExactlyOnce>] Args of string list
    | [<AltCommandLine("-f")>] Force

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args _ -> "name of the agent to delete"
            | Force -> "delete without confirmation"

[<RequireQualifiedAccess>]
type AgentCommand =
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<NoArgs>
    | [<CliPrefix(CliPrefix.None)>] Create of ParseResults<AgentCreateArgs>
    | [<CliPrefix(CliPrefix.None)>] Set of ParseResults<AgentSetArgs>
    | [<CliPrefix(CliPrefix.None)>] Get of ParseResults<AgentGetArgs>
    | [<CliPrefix(CliPrefix.None)>] Read of ParseResults<AgentReadArgs>
    | [<CliPrefix(CliPrefix.None)>] Send of ParseResults<AgentSendArgs>
    | [<CliPrefix(CliPrefix.None); CustomCommandLine("edit-script")>] Edit_Script of ParseResults<AgentEditScriptArgs>
    | [<CliPrefix(CliPrefix.None)>] Delete of ParseResults<AgentDeleteArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | List _ -> "list all available agents and their configurations"
            | Create _ -> "create a new agent with the specified configuration"
            | Set _ -> "set a configuration value for an agent"
            | Get _ -> "get a configuration value for an agent"
            | Read _ -> "read information about an agent"
            | Send _ -> "send a message to an agent and display the response"
            | Edit_Script _ -> "open the agent's invocation script in $EDITOR"
            | Delete _ -> "delete an agent and its configuration"

// ------------------------------------------------------------------- top ---

[<RequireQualifiedAccess>]
type CliArgs =
    | [<CliPrefix(CliPrefix.None)>] Version of ParseResults<NoArgs>
    | [<CliPrefix(CliPrefix.None)>] Config of ParseResults<ConfigCommand>
    | [<CliPrefix(CliPrefix.None)>] Memory of ParseResults<MemoryCommand>
    | [<CliPrefix(CliPrefix.None)>] Script of ParseResults<ScriptCommand>
    | [<CliPrefix(CliPrefix.None)>] Agent of ParseResults<AgentCommand>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Version _ -> "show the version of SkogCli"
            | Config _ -> "manage SkogCli configuration"
            | Memory _ -> "knowledge management for SkogAI agents (wraps basic-memory)"
            | Script _ -> "manage and run small user-authored scripts"
            | Agent _ -> "interact with SkogAI agents"
