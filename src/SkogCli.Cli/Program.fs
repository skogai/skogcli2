module SkogCli.Cli.Program

open System
open Argu
open SkogCli.Cli.Args
open SkogCli.Core
open SkogCli.Core.Json

let private confirm (prompt: string) : bool =
    Console.Write($"{prompt} [y/N]: ")

    match Console.ReadLine() with
    | null -> false
    | line -> [ "y"; "yes" ] |> List.contains (line.Trim().ToLowerInvariant())

let private printSet (key: string) (value: JsonValue) =
    if JsonValue.isContainer value then
        printfn "Set %s = " key
        printfn "%s" (JsonValue.serialize value)
    else
        printfn "Set %s = %s" key (JsonValue.display value)

let private printGet (key: string) (value: JsonValue) (raw: bool) (json: bool) =
    if json then
        printfn "%s" (JsonValue.serialize value)
    elif raw then
        printfn "%s" (JsonValue.display value)
    elif JsonValue.isContainer value then
        printfn "%s = " key
        printfn "%s" (JsonValue.serialize value)
    else
        printfn "%s = %s" key (JsonValue.display value)

// ------------------------------------------------------------------ config

let private handleConfig (results: ParseResults<ConfigCommand>) =
    match results.GetSubCommand() with
    | ConfigCommand.Show _ -> printfn "%s" (JsonValue.serialize (Settings.load ()))
    | ConfigCommand.List sub ->
        let envOnly = sub.Contains ConfigListArgs.EnvOnly
        let pairs = Settings.load () |> JsonValue.flatten ""

        let pairs =
            if envOnly then pairs |> List.filter (fun (k, _) -> k.Contains ".env.") else pairs

        printfn "%s" (if envOnly then "Environment variables:" else "Configuration settings:")

        for k, v in pairs |> List.sortBy fst do
            let displayed =
                match v with
                | JString s -> $"\"{s}\""
                | JNull -> "null"
                | _ -> JsonValue.display v

            printfn "  %s = %s" k displayed
    | ConfigCommand.Get sub ->
        let key = sub.GetResult ConfigGetArgs.Args |> String.concat " "

        match Settings.get key with
        | None -> eprintfn "Error: Key '%s' not found in configuration." key
        | Some v -> printGet key v (sub.Contains ConfigGetArgs.Raw) (sub.Contains ConfigGetArgs.Json)
    | ConfigCommand.Set sub ->
        match sub.GetResult ConfigSetArgs.Args with
        | [ key; value ] ->
            let parsed = JsonValue.parseValueLoosely value
            Settings.set key parsed
            printSet key parsed
        | _ -> eprintfn "Error: 'config set' takes exactly a <key> and a <value>."
    | ConfigCommand.Reset sub ->
        if sub.Contains ConfigResetArgs.Yes || confirm "Are you sure you want to reset all settings to defaults?" then
            Settings.reset ()
            printfn "Configuration reset to defaults."
        else
            printfn "Reset cancelled."
    | ConfigCommand.Backup _ ->
        let backups =
            [ Settings.createBackup (Paths.configFile ()); Settings.createBackup (Paths.credentialsFile ()) ]
            |> List.choose id

        if backups.IsEmpty then
            printfn "No configuration files to backup."
        else
            printfn "Backups created successfully:"
            for b in backups do
                printfn "  - %s" b
    | ConfigCommand.Restore sub ->
        let explicit_ = sub.TryGetResult ConfigRestoreArgs.Args |> Option.bind List.tryHead

        let backupFile =
            explicit_
            |> Option.orElseWith (fun () -> Settings.listBackups () |> List.tryHead |> Option.map (fun (p, _, _) -> p))

        match backupFile with
        | None -> eprintfn "Error: No backups found."
        | Some file ->
            if sub.Contains ConfigRestoreArgs.Yes || confirm $"Are you sure you want to restore from {file}?" then
                match Settings.restoreFrom file with
                | Ok() -> printfn "Configuration restored from %s" file
                | Error e -> eprintfn "Error: %s" e
            else
                printfn "Restore cancelled."
    | ConfigCommand.List_Backups _ ->
        let backups = Settings.listBackups ()

        if backups.IsEmpty then
            printfn "No backups found."
        else
            printfn "Available backups:"

            backups
            |> List.iteri (fun i (path, time, size) ->
                printfn
                    "%d. %s (%s, %.1f KB)"
                    (i + 1)
                    (IO.Path.GetFileName path)
                    (time.ToString "yyyy-MM-dd HH:mm:ss")
                    (float size / 1024.0))
    | ConfigCommand.Chat_History sub ->
        if sub.Contains ConfigChatHistoryArgs.Clear then
            if confirm "Are you sure you want to clear all chat history?" then
                Settings.clearHistory ()
                printfn "Chat history cleared."
            else
                printfn "Cancelled."
        else
            let limit = sub.TryGetResult ConfigChatHistoryArgs.Limit |> Option.defaultValue 10
            let history = Settings.getHistory (Some limit)

            if history.IsEmpty then
                printfn "No chat history found."
            elif sub.Contains ConfigChatHistoryArgs.Json then
                printfn "%s" (JsonValue.serialize (JArray history))
            else
                printfn "Chat history (showing %d of %d items):" history.Length (Settings.getHistory None).Length

                history
                |> List.iteri (fun i item ->
                    let timestamp =
                        match JsonValue.getPath [ "timestamp" ] item with
                        | Some(JString s) -> s
                        | _ -> "unknown"

                    printfn "%d. %s" (i + 1) timestamp

                    match JsonValue.getPath [ "session_id" ] item with
                    | Some(JString s) -> printfn "  Session: %s" s
                    | _ -> ()

                    match JsonValue.getPath [ "message" ] item with
                    | Some(JString s) -> printfn "  Message: %s" (if s.Length > 50 then s.Substring(0, 50) + "..." else s)
                    | _ -> ()

                    printfn "")
    | ConfigCommand.Export_Env sub ->
        let namespaces =
            sub.TryGetResult ConfigExportEnvArgs.Namespace
            |> Option.map (fun s -> s.Split(',') |> Array.map (fun x -> x.Trim()) |> List.ofArray)

        let pairs = Settings.envExportPairs namespaces

        if pairs.IsEmpty then
            let suffix =
                namespaces
                |> Option.map (fun ns ->
                    let joined = String.concat "," ns
                    $" in namespace '{joined}'")
                |> Option.defaultValue ""

            eprintfn "No environment variables found%s." suffix
        else
            let lines =
                pairs
                |> List.sortBy fst
                |> List.map (fun (k, v) ->
                    let escaped = v.Replace("\"", "\\\"")
                    $"export {k}=\"{escaped}\"")

            match sub.TryGetResult ConfigExportEnvArgs.Output with
            | Some path ->
                IO.File.WriteAllText(path, String.concat "\n" lines + "\n")
                printfn "Environment variables exported to: %s" path
            | None -> lines |> List.iter (printfn "%s")

// ------------------------------------------------------------------ memory

let private printMemoryResult (r: SkogCli.Memory.Memory.MemoryResult) =
    if r.Stdout <> "" then
        printfn "%s" r.Stdout

    if r.ExitCode <> 0 then
        if r.Stderr <> "" then
            eprintfn "Error: %s" r.Stderr

        exit r.ExitCode

let private handleMemory (results: ParseResults<MemoryCommand>) =
    match results.GetSubCommand() with
    | MemoryCommand.Write sub ->
        match sub.GetResult MemoryWriteArgs.Args with
        | [ title; folder ] ->
            let content =
                match sub.TryGetResult MemoryWriteArgs.Content with
                | Some c -> c
                | None ->
                    printfn "Enter note content (Ctrl+D to finish):"
                    Console.In.ReadToEnd()

            let result =
                SkogCli.Memory.Memory.writeNote
                    title
                    folder
                    content
                    (sub.TryGetResult MemoryWriteArgs.Tag)
                    (sub.TryGetResult MemoryWriteArgs.Project)

            if result.ExitCode = 0 then
                printfn "Note saved: %s in %s" title folder
            else
                eprintfn "Error: %s" result.Stderr
                exit result.ExitCode
        | _ -> eprintfn "Error: 'memory write' takes exactly a <title> and a <folder>."
    | MemoryCommand.Read sub ->
        let identifier = sub.GetResult MemoryReadArgs.Args |> String.concat " "

        SkogCli.Memory.Memory.readNote
            identifier
            (sub.TryGetResult MemoryReadArgs.Page |> Option.defaultValue 1)
            (sub.TryGetResult MemoryReadArgs.Page_Size |> Option.defaultValue 10)
            (sub.TryGetResult MemoryReadArgs.Project)
        |> printMemoryResult
    | MemoryCommand.Search sub ->
        let opts: SkogCli.Memory.Memory.SearchOptions =
            { Query = sub.GetResult MemorySearchArgs.Args |> String.concat " "
              Permalink = sub.Contains MemorySearchArgs.Permalink
              Title = sub.Contains MemorySearchArgs.Title
              AfterDate = sub.TryGetResult MemorySearchArgs.After_Date
              BeforeDate = sub.TryGetResult MemorySearchArgs.Before_Date
              Page = sub.TryGetResult MemorySearchArgs.Page |> Option.defaultValue 1
              PageSize = sub.TryGetResult MemorySearchArgs.Page_Size |> Option.defaultValue 10
              Project = sub.TryGetResult MemorySearchArgs.Project }

        SkogCli.Memory.Memory.searchNotes opts |> printMemoryResult
    | MemoryCommand.List sub ->
        let opts: SkogCli.Memory.Memory.ListOptions =
            { ActivityType = sub.TryGetResult MemoryListArgs.Type
              Folder = sub.TryGetResult MemoryListArgs.Folder
              Depth = sub.TryGetResult MemoryListArgs.Depth |> Option.defaultValue 1
              Timeframe = sub.TryGetResult MemoryListArgs.Timeframe |> Option.defaultValue "7d"
              Page = sub.TryGetResult MemoryListArgs.Page |> Option.defaultValue 1
              PageSize = sub.TryGetResult MemoryListArgs.Page_Size |> Option.defaultValue 10
              MaxRelated = sub.TryGetResult MemoryListArgs.Max_Related |> Option.defaultValue 5
              Project = sub.TryGetResult MemoryListArgs.Project }

        SkogCli.Memory.Memory.recentActivity opts |> printMemoryResult
    | MemoryCommand.Sync sub ->
        SkogCli.Memory.Memory.sync
            (sub.TryGetResult MemorySyncArgs.Project)
            (sub.Contains MemorySyncArgs.Force)
            (sub.Contains MemorySyncArgs.Dry_Run)
            (sub.Contains MemorySyncArgs.Verbose)
        |> printMemoryResult
    | MemoryCommand.Status sub ->
        let asJson = (sub.TryGetResult MemoryStatusArgs.Format |> Option.defaultValue "table") = "json"
        SkogCli.Memory.Memory.projectInfo (sub.TryGetResult MemoryStatusArgs.Project) asJson |> printMemoryResult
    | MemoryCommand.Bm sub ->
        let passthrough = sub.TryGetResult MemoryBmArgs.Args |> Option.defaultValue []
        SkogCli.Memory.Memory.runBasicMemory passthrough |> printMemoryResult

// ------------------------------------------------------------------ script

open SkogCli.Scripts.Scripts

let private locationLabel =
    function
    | UserScript -> "user"
    | GlobalScript -> "global"

let private findOrError (name: string) (includeGlobal: bool) : ScriptInfo option =
    match findScript name includeGlobal with
    | Some s -> Some s
    | None ->
        eprintfn "Error: Script '%s' not found." name
        None

let private handleScript (results: ParseResults<ScriptCommand>) =
    match results.GetSubCommand() with
    | ScriptCommand.List sub ->
        let includeGlobal = not (sub.Contains ScriptListArgs.No_Global)
        let scripts = listScripts includeGlobal

        if scripts.IsEmpty then
            printfn "No custom scripts found."
            printfn "Add scripts to %s to get started." (Paths.userScriptsDir ())
        else
            printfn "Available custom scripts:"

            for s in scripts do
                if sub.Contains ScriptListArgs.Metadata then
                    let meta = getMetadata s

                    let desc =
                        match meta |> Map.tryFind "description" with
                        | Some(JString d) -> d
                        | _ -> ""

                    printfn "  %s - %s (%s) %s" s.Name (kindLabel s.Kind) (locationLabel s.Location) desc
                else
                    printfn "  %s - %s script (%s)" s.Name (kindLabel s.Kind) (locationLabel s.Location)
    | ScriptCommand.Run sub ->
        match sub.GetResult ScriptRunArgs.Args with
        | name :: scriptArgs ->
            match findOrError name (not (sub.Contains ScriptRunArgs.No_Global)) with
            | None -> exit 1
            | Some script ->
                let outcome = runScript script scriptArgs
                if outcome.Stdout <> "" then Console.Out.Write outcome.Stdout

                if outcome.ExitCode <> 0 then
                    Console.Error.Write outcome.Stderr
                    exit outcome.ExitCode
        | [] -> eprintfn "Error: 'script run' requires a script name."
    | ScriptCommand.Create sub ->
        match sub.GetResult ScriptCreateArgs.Args with
        | name :: _ ->
            let scriptType = sub.TryGetResult ScriptCreateArgs.Type |> Option.defaultValue "shell"
            let template = sub.TryGetResult ScriptCreateArgs.Template |> Option.defaultValue "basic"
            let isGlobal = sub.Contains ScriptCreateArgs.Global
            let description = sub.TryGetResult ScriptCreateArgs.Description |> Option.defaultValue ""

            let createResult =
                match createScript name scriptType template isGlobal description with
                | AlreadyExists info when confirm $"Script '{name}' already exists. Overwrite?" ->
                    IO.File.Delete info.Path
                    removeMetadata info
                    createScript name scriptType template isGlobal description
                | other -> other

            match createResult with
            | Created info -> printfn "Created %s script: %s" (locationLabel info.Location) info.Path
            | AlreadyExists _ -> ()
            | TemplateMissing msg -> eprintfn "Error: %s" msg

            if sub.Contains ScriptCreateArgs.Edit then
                match findScript name true with
                | Some info ->
                    let editor = Environment.GetEnvironmentVariable "EDITOR" |> Option.ofObj |> Option.defaultValue "vi"
                    Proc.runInteractive editor [ info.Path ] |> ignore
                | None -> ()
        | [] -> eprintfn "Error: 'script create' requires a name."
    | ScriptCommand.Edit sub ->
        let name = sub.GetResult ScriptEditArgs.Args |> String.concat " "

        match findOrError name (not (sub.Contains ScriptEditArgs.No_Global)) with
        | None -> exit 1
        | Some script ->
            let editor = Environment.GetEnvironmentVariable "EDITOR" |> Option.ofObj |> Option.defaultValue "vi"
            let exitCode = Proc.runInteractive editor [ script.Path ]

            if exitCode = 0 then
                updateMetadata script [ "last_edited", JString(DateTime.UtcNow.ToString "o") ]
                printfn "Script edited successfully."
            else
                eprintfn "Error: Editor exited with code %d" exitCode
    | ScriptCommand.Remove sub ->
        let name = sub.GetResult ScriptRemoveArgs.Args |> String.concat " "

        match findOrError name (not (sub.Contains ScriptRemoveArgs.No_Global)) with
        | None -> exit 1
        | Some script ->
            if
                sub.Contains ScriptRemoveArgs.Force
                || confirm $"Are you sure you want to remove {locationLabel script.Location} script '{name}'?"
            then
                removeScript script
                printfn "Removed %s script: %s" (locationLabel script.Location) name
            else
                printfn "Cancelled."
    | ScriptCommand.Info sub ->
        let name = sub.GetResult ScriptInfoArgs.Args |> String.concat " "

        match findOrError name (not (sub.Contains ScriptInfoArgs.No_Global)) with
        | None -> exit 1
        | Some script ->
            printfn "Script: %s" script.Name
            printfn "Path: %s" script.Path
            printfn "Type: %s" (kindLabel script.Kind)
            printfn "Location: %s" (locationLabel script.Location)
            let meta = getMetadata script

            if meta.IsEmpty then
                printfn ""
                printfn "No metadata available for this script."
            else
                printfn ""
                printfn "Metadata:"
                for KeyValue(k, v) in meta do
                    printfn "  %s: %s" k (JsonValue.display v)
    | ScriptCommand.Code sub ->
        let name = sub.GetResult ScriptCodeArgs.Args |> String.concat " "

        match findOrError name (not (sub.Contains ScriptCodeArgs.No_Global)) with
        | None -> exit 1
        | Some script ->
            let newContent =
                match sub.TryGetResult ScriptCodeArgs.File with
                | Some path -> Some(IO.File.ReadAllText path)
                | None -> sub.TryGetResult ScriptCodeArgs.Content

            match newContent with
            | Some content ->
                match sub.TryGetResult ScriptCodeArgs.Output with
                | Some outPath -> IO.File.WriteAllText(outPath, content)
                | None ->
                    IO.File.WriteAllText(script.Path, content)
                    updateMetadata script [ "last_edited", JString(DateTime.UtcNow.ToString "o") ]
                    printfn "Updated %s" script.Path
            | None ->
                let content = IO.File.ReadAllText script.Path

                match sub.TryGetResult ScriptCodeArgs.Output with
                | Some outPath -> IO.File.WriteAllText(outPath, content)
                | None -> printfn "%s" content
    | ScriptCommand.Copy sub ->
        match sub.GetResult ScriptCopyArgs.Args with
        | [ source; destination ] ->
            match findOrError source (not (sub.Contains ScriptCopyArgs.No_Global_Source)) with
            | None -> exit 1
            | Some src ->
                match copyScript src destination (sub.Contains ScriptCopyArgs.Global_Dest) with
                | Ok dest -> printfn "Copied to new %s script: %s" (locationLabel dest.Location) dest.Path
                | Error e -> eprintfn "Error: %s" e
        | _ -> eprintfn "Error: 'script copy' takes exactly a <source> and a <destination>."
    | ScriptCommand.Search sub ->
        let pattern = sub.GetResult ScriptSearchArgs.Args |> String.concat " "
        let includeGlobal = not (sub.Contains ScriptSearchArgs.No_Global)

        let results =
            searchScripts pattern (sub.Contains ScriptSearchArgs.Regex) (sub.Contains ScriptSearchArgs.Case_Sensitive) includeGlobal

        if results.IsEmpty then
            printfn "No matches found."
        else
            let lines =
                results
                |> List.collect (fun r ->
                    let header = $"{r.Script.Name} ({locationLabel r.Script.Location}): {r.Script.Path}"
                    let matchLines = r.Matches |> List.map (fun m -> $"  Line {m.LineNumber}: {m.Line}")
                    header :: matchLines @ [ "" ])

            match sub.TryGetResult ScriptSearchArgs.Output with
            | Some path ->
                IO.File.WriteAllText(path, String.concat "\n" lines + "\n")
                printfn "Search results written to: %s" path
            | None ->
                lines |> List.iter (printfn "%s")
                printfn "Found matches in %d scripts." results.Length
    | ScriptCommand.Templates _ ->
        let templates = listAvailableTemplates ()

        if templates.IsEmpty then
            printfn "No templates available."
        else
            for KeyValue(scriptType, names) in templates do
                printfn "%s:" scriptType
                for n in names |> List.sort do
                    printfn "  %s" n

// ------------------------------------------------------------------- agent

open SkogCli.Agent.Agent

let private handleAgent (results: ParseResults<AgentCommand>) =
    match results.GetSubCommand() with
    | AgentCommand.List _ ->
        let names = listAgentNames ()

        if names.IsEmpty then
            printfn "No agents configured."
            printfn "Use 'skogcli agent create' to create a new agent."
        else
            printfn "Available agents:"

            for name in names do
                let cfg = getAgentConfig name
                let model = cfg.Model |> Option.defaultValue "not-specified"
                printfn "  %s" name
                printfn "    Model: %s" model
                cfg.Description |> Option.iter (printfn "    Description: %s")
    | AgentCommand.Create sub ->
        match sub.GetResult AgentCreateArgs.Args with
        | name :: _ ->
            match
                createAgent
                    name
                    (sub.TryGetResult AgentCreateArgs.Model)
                    (sub.TryGetResult AgentCreateArgs.System)
                    (sub.TryGetResult AgentCreateArgs.Description)
                    (sub.TryGetResult AgentCreateArgs.Command)
            with
            | AgentCreated(_, path) ->
                printfn "Created agent: %s" name
                printfn "Script created at: %s" path
            | AgentAlreadyExists ->
                printfn "Agent '%s' already exists." name
                printfn "Use 'skogcli agent set' to update its configuration."
        | [] -> eprintfn "Error: 'agent create' requires a name."
    | AgentCommand.Set sub ->
        match sub.GetResult AgentSetArgs.Args with
        | [ key; value ] ->
            match resolveKey key (sub.TryGetResult AgentSetArgs.Agent) with
            | Error e -> eprintfn "Error: %s" e
            | Ok fullKey ->
                let parsed = JsonValue.parseValueLoosely value
                Settings.set fullKey parsed
                printSet fullKey parsed
        | _ -> eprintfn "Error: 'agent set' takes exactly a <key> and a <value>."
    | AgentCommand.Get sub ->
        let key = sub.GetResult AgentGetArgs.Args |> String.concat " "

        match resolveKey key (sub.TryGetResult AgentGetArgs.Agent) with
        | Error e -> eprintfn "Error: %s" e
        | Ok fullKey ->
            match Settings.get fullKey with
            | None -> eprintfn "Error: Key '%s' not found in configuration." fullKey
            | Some v -> printGet fullKey v (sub.Contains AgentGetArgs.Raw) (sub.Contains AgentGetArgs.Json)
    | AgentCommand.Read sub ->
        let positional = sub.TryGetResult AgentReadArgs.Args |> Option.bind List.tryHead
        let agentOpt = sub.TryGetResult AgentReadArgs.Agent

        match agentOpt |> Option.orElse positional with
        | None -> eprintfn "Error: Agent name must be provided either as an argument or with --agent"
        | Some name ->
            let message = $"Hello world from agent: {name}"

            if sub.Contains AgentReadArgs.Json then
                let cfg = getAgentConfig name

                let cfgJson =
                    [ cfg.Model |> Option.map (fun v -> "model", JString v)
                      cfg.SystemPrompt |> Option.map (fun v -> "system_prompt", JString v)
                      cfg.Description |> Option.map (fun v -> "description", JString v) ]
                    |> List.choose id
                    |> Map.ofList
                    |> JObject

                printfn
                    "%s"
                    (JsonValue.serialize (
                        JObject(Map.ofList [ "agent", JString name; "message", JString message; "config", cfgJson ])
                    ))
            elif sub.Contains AgentReadArgs.Raw then
                printfn "%s" message
            else
                printfn "Agent: %s" name
                printfn "%s" message
    | AgentCommand.Send sub ->
        let message = sub.GetResult AgentSendArgs.Args |> String.concat " "
        let agentName = sub.TryGetResult AgentSendArgs.Agent |> Option.defaultValue "default"
        let result = send agentName message (sub.TryGetResult AgentSendArgs.Model)

        if sub.Contains AgentSendArgs.Json then
            printfn
                "%s"
                (JsonValue.serialize (
                    JObject(
                        Map.ofList
                            [ "agent", JString agentName
                              "model", JString result.Model
                              "message", JString message
                              "response", JString result.Response ]
                    )
                ))
        elif sub.Contains AgentSendArgs.Raw then
            printfn "%s" result.Response
        else
            printfn "Response from %s:" agentName
            printfn "%s" result.Response
    | AgentCommand.Edit_Script sub ->
        let name = sub.GetResult AgentEditScriptArgs.Args |> String.concat " "
        let path = scriptPath name

        if not (IO.File.Exists path) then
            let cfg = getAgentConfig name
            let command = cfg.CommandTemplate |> Option.defaultValue $"echo \"Agent {name} is responding to: {{message}}\""
            writeAgentScript name command |> ignore
            printfn "Created script: %s" path

        let editor = Environment.GetEnvironmentVariable "EDITOR" |> Option.ofObj |> Option.defaultValue "vim"
        Proc.runInteractive editor [ path ] |> ignore
        printfn "Edited script: %s" path
    | AgentCommand.Delete sub ->
        let name = sub.GetResult AgentDeleteArgs.Args |> String.concat " "
        let force = sub.Contains AgentDeleteArgs.Force

        match deleteAgent name force (fun () -> confirm $"Are you sure you want to delete agent '{name}'?") with
        | Ok() -> printfn "Deleted agent: %s" name
        | Error e -> eprintfn "%s" e

// --------------------------------------------------------------------- main

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<CliArgs>(programName = "skogcli")

    try
        let results = parser.ParseCommandLine(argv, raiseOnUsage = true)

        match results.GetAllResults() with
        | [] ->
            printfn "%s" (parser.PrintUsage())
            0
        | _ ->
            match results.GetSubCommand() with
            | CliArgs.Version _ ->
                let version =
                    Reflection.Assembly.GetExecutingAssembly().GetName().Version
                    |> Option.ofObj
                    |> Option.map string
                    |> Option.defaultValue "development"

                printfn "SkogCli v%s" version
            | CliArgs.Config sub -> handleConfig sub
            | CliArgs.Memory sub -> handleMemory sub
            | CliArgs.Script sub -> handleScript sub
            | CliArgs.Agent sub -> handleAgent sub

            0
    with :? ArguParseException as ex ->
        printfn "%s" ex.Message
        if ex.ErrorCode = ErrorCode.HelpText then 0 else 1
