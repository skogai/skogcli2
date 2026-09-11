/// Configuration management: a JSON-backed settings tree with dot-path
/// get/set, environment-variable overrides, backups and env-var export.
///
/// This is a from-scratch, typed re-design of skogcli's Python `settings.py`
/// (the "$" SkogAI-notation shorthand and the legacy `chat.history` migration
/// path were dropped as historical cruft rather than ported).
module SkogCli.Core.Settings

open System
open System.IO
open SkogCli.Core.Json

let configVersion = 1

let private defaultSettings: JsonValue =
    JObject(
        Map.ofList
            [ "settings",
              JObject(
                  Map.ofList
                      [ "meta", JObject(Map.ofList [ "version", JNumber(float configVersion) ])
                        "module", JObject(Map.ofList [ "history", JArray [] ]) ]
              )
              "credentials", JObject Map.empty ]
    )

let private splitKey (key: string) = key.Split('.') |> List.ofArray

/// Read a file as JsonValue, returning None if it doesn't exist and Error on malformed JSON.
let private tryReadJson (path: string) : Result<JsonValue option, string> =
    if not (File.Exists path) then
        Ok None
    else
        match JsonValue.parse (File.ReadAllText path) with
        | Ok v -> Ok(Some v)
        | Error e -> Error e

let private writeJson (path: string) (v: JsonValue) =
    File.WriteAllText(path, JsonValue.serialize v)

/// Create a timestamped backup copy of a file, if it exists. Returns the backup path.
let createBackup (path: string) : string option =
    if not (File.Exists path) then
        None
    else
        let stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss")
        let backupPath = Path.Combine(Paths.backupDir (), $"{Path.GetFileName path}.{stamp}.bak")
        File.Copy(path, backupPath, overwrite = true)
        Some backupPath

let private latestBackupFor (fileNameContains: string) : string option =
    let dir = Paths.backupDir ()

    if not (Directory.Exists dir) then
        None
    else
        Directory.GetFiles(dir, "*.bak")
        |> Array.filter (fun f -> Path.GetFileName(f).Contains(fileNameContains: string))
        |> Array.sortByDescending File.GetLastWriteTimeUtc
        |> Array.tryHead

/// Load settings from disk, creating the default config on first run and
/// recovering from the most recent backup if the file is corrupted.
let load () : JsonValue =
    match tryReadJson (Paths.configFile ()) with
    | Ok(Some v) -> v
    | Ok None ->
        writeJson (Paths.configFile ()) defaultSettings
        defaultSettings
    | Error _ ->
        match latestBackupFor "config.json" with
        | Some backup ->
            match JsonValue.parse (File.ReadAllText backup) with
            | Ok v -> v
            | Error _ -> defaultSettings
        | None -> defaultSettings

let private loadCredentials () : JsonValue =
    match tryReadJson (Paths.credentialsFile ()) with
    | Ok(Some v) -> v
    | _ -> JObject Map.empty

let save (settings: JsonValue) : unit =
    createBackup (Paths.configFile ()) |> ignore

    let settings =
        JsonValue.setPath [ "settings"; "meta"; "last_updated" ] (JString(DateTime.UtcNow.ToString "o")) settings

    // Credentials live in their own file, never inline in config.json. `set` is the
    // only place that writes credentials.json, and it does so directly against the
    // full on-disk credentials store - `settings.credentials` here is never more than
    // whatever the caller happened to pass in (often nothing, or a single just-set
    // key), so writing it out would silently clobber every other stored credential.
    let onDisk = JsonValue.setPath [ "credentials" ] (JObject Map.empty) settings
    writeJson (Paths.configFile ()) onDisk

/// Environment-variable override for a dotted key, e.g. "agent.default_model"
/// -> SKOGAI_AGENT_DEFAULT_MODEL (SKOGAI_TEST_* takes precedence, for tests).
let private envOverride (key: string) : JsonValue option =
    let envName prefix = prefix + key.ToUpperInvariant().Replace(".", "_")

    let fromEnv name =
        match Environment.GetEnvironmentVariable(name: string) with
        | null -> None
        | v -> Some v

    let raw = fromEnv (envName "SKOGAI_TEST_") |> Option.orElseWith (fun () -> fromEnv (envName "SKOGAI_"))

    raw
    |> Option.map (fun v ->
        match v.ToLowerInvariant() with
        | "true"
        | "yes"
        | "1" -> JBool true
        | "false"
        | "no"
        | "0" -> JBool false
        | "null"
        | "none" -> JNull
        | _ ->
            match Double.TryParse(v, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, n -> JNumber n
            | false, _ -> JString v)

/// Get a setting by dotted key, honouring environment overrides and the
/// separate credentials store for "credentials.*" keys.
let get (key: string) : JsonValue option =
    match envOverride key with
    | Some v -> Some v
    | None ->
        if key.StartsWith "credentials." then
            let credKey = key.Substring "credentials.".Length
            JsonValue.getPath (splitKey credKey) (loadCredentials ())
        else
            JsonValue.getPath (splitKey key) (load ())

/// Set a setting by dotted key. Credentials are routed to the separate
/// credentials store; everything else is persisted to config.json.
let set (key: string) (value: JsonValue) : unit =
    if key.StartsWith "credentials." then
        let credKey = key.Substring "credentials.".Length
        let creds = JsonValue.setPath (splitKey credKey) value (loadCredentials ())
        writeJson (Paths.credentialsFile ()) creds
        save (load ())
    else
        let settings = JsonValue.setPath (splitKey key) value (load ())
        save settings

let reset () : unit =
    createBackup (Paths.configFile ()) |> ignore
    createBackup (Paths.credentialsFile ()) |> ignore
    save defaultSettings

/// All leaf keys in the config, in dotted form - used for shell completion and "config list".
let allKeys () : string list =
    load () |> JsonValue.flatten "" |> List.map fst |> List.sort

let listBackups () : (string * DateTime * int64) list =
    let dir = Paths.backupDir ()

    if not (Directory.Exists dir) then
        []
    else
        Directory.GetFiles(dir, "*.bak")
        |> Array.map (fun f ->
            let info = FileInfo f
            f, info.LastWriteTime, info.Length)
        |> Array.sortByDescending (fun (_, t, _) -> t)
        |> List.ofArray

let restoreFrom (backupPath: string) : Result<unit, string> =
    if not (File.Exists backupPath) then
        Error $"Backup file not found: {backupPath}"
    else
        let target =
            if Path.GetFileName(backupPath).Contains "credentials" then
                Paths.credentialsFile ()
            else
                Paths.configFile ()

        createBackup target |> ignore
        File.Copy(backupPath, target, overwrite = true)
        Ok()

// --- Chat / interaction history -------------------------------------------------

let addHistoryItem (item: JsonValue) : unit =
    let settings = load ()
    let history = JsonValue.getPath [ "settings"; "module"; "history" ] settings

    let items =
        match history with
        | Some(JArray xs) -> xs
        | _ -> []

    let item =
        match JsonValue.getPath [ "timestamp" ] item with
        | Some _ -> item
        | None -> JsonValue.setPath [ "timestamp" ] (JString(DateTime.UtcNow.ToString "o")) item

    let maxItems =
        match JsonValue.getPath [ "settings"; "module"; "max_history_items" ] settings with
        | Some(JNumber n) -> int n
        | _ -> 100

    let updated =
        let all = items @ [ item ]
        if all.Length > maxItems then all |> List.skip (all.Length - maxItems) else all

    save (JsonValue.setPath [ "settings"; "module"; "history" ] (JArray updated) settings)

let getHistory (limit: int option) : JsonValue list =
    let settings = load ()

    let items =
        match JsonValue.getPath [ "settings"; "module"; "history" ] settings with
        | Some(JArray xs) -> xs
        | _ -> []

    let timestampOf item =
        match JsonValue.getPath [ "timestamp" ] item with
        | Some(JString s) -> s
        | _ -> ""

    let sorted = items |> List.sortByDescending timestampOf

    match limit with
    | Some n -> sorted |> List.truncate n
    | None -> sorted

let clearHistory () : unit =
    createBackup (Paths.configFile ()) |> ignore
    let settings = load ()
    save (JsonValue.setPath [ "settings"; "module"; "history" ] (JArray []) settings)

// --- Environment variable export -------------------------------------------------

/// Extract `*.env.NAME` leaves from the config, optionally restricted to one or
/// more comma-separated namespaces (later namespaces override earlier ones).
let envExportPairs (namespaces: string list option) : (string * string) list =
    let settings = load ()
    let allLeaves = JsonValue.flatten "" settings

    let envLeaves =
        allLeaves
        |> List.choose (fun (k, v) ->
            if k.Contains ".env." then
                match v with
                | JNull -> None
                | _ -> Some(k, v)
            else
                None)

    match namespaces with
    | None -> envLeaves |> List.map (fun (k, v) -> k.Split(".env.").[1], JsonValue.display v)
    | Some namespaces ->
        let result = System.Collections.Generic.Dictionary<string, string>()

        for ns in namespaces do
            let prefix = $"{ns}.env."

            for k, v in envLeaves do
                if k.StartsWith prefix then
                    result.[k.Substring prefix.Length] <- JsonValue.display v

        result |> Seq.map (fun kv -> kv.Key, kv.Value) |> List.ofSeq
