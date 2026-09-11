/// Script management: create, list, run, edit, inspect and search small
/// user-authored shell/Python helper scripts, plus lightweight JSON metadata
/// (description, run counts, timestamps) tracked per script path.
module SkogCli.Scripts.Scripts

open System
open System.IO
open SkogCli.Core.Json
open SkogCli.Core

type ScriptKind =
    | Python
    | Shell
    | Executable
    | UnknownKind

type Location =
    | UserScript
    | GlobalScript

type ScriptInfo =
    { Path: string
      Name: string
      Kind: ScriptKind
      Location: Location }

let private kindOf (path: string) : ScriptKind =
    match Path.GetExtension(path).ToLowerInvariant() with
    | ".py" -> Python
    | ".sh" -> Shell
    | _ ->
        try
            let mode = File.GetUnixFileMode path
            if mode.HasFlag UnixFileMode.UserExecute then Executable else UnknownKind
        with _ ->
            UnknownKind

let kindLabel =
    function
    | Python -> "Python"
    | Shell -> "Shell"
    | Executable -> "Executable"
    | UnknownKind -> "Unknown"

let private isExecutable (path: string) : bool =
    try
        (File.GetUnixFileMode path).HasFlag UnixFileMode.UserExecute
    with _ ->
        false

let private makeExecutable (path: string) =
    try
        let mode = File.GetUnixFileMode path

        File.SetUnixFileMode(
            path,
            mode
            ||| UnixFileMode.UserExecute
            ||| UnixFileMode.GroupExecute
            ||| UnixFileMode.OtherExecute
        )
    with _ ->
        ()

// --- Discovery --------------------------------------------------------------

let private scriptsIn (dir: string) (loc: Location) : ScriptInfo list =
    if not (Directory.Exists dir) then
        []
    else
        Directory.GetFiles dir
        |> Array.map (fun p ->
            { Path = p
              Name = Path.GetFileNameWithoutExtension p
              Kind = kindOf p
              Location = loc })
        |> List.ofArray

/// List every script known to SkogCli: user scripts always, global scripts when requested.
let listScripts (includeGlobal: bool) : ScriptInfo list =
    let user = scriptsIn (Paths.userScriptsDir ()) UserScript

    let global_ =
        if includeGlobal then
            scriptsIn (Paths.globalScriptsDir ()) GlobalScript
        else
            []

    user @ global_

let scriptNames () : string list = listScripts true |> List.map (fun s -> s.Name)

/// Find a script by bare name, checking the user directory first, then global.
let findScript (name: string) (includeGlobal: bool) : ScriptInfo option =
    listScripts includeGlobal |> List.tryFind (fun s -> s.Name = name)

// --- Metadata -----------------------------------------------------------------

let private loadMetadata () : Map<string, JsonValue> =
    let file = Paths.scriptMetadataFile ()

    if not (File.Exists file) then
        Map.empty
    else
        match JsonValue.parse (File.ReadAllText file) with
        | Ok(JObject fields) -> fields
        | _ -> Map.empty

let private saveMetadata (m: Map<string, JsonValue>) =
    File.WriteAllText(Paths.scriptMetadataFile (), JsonValue.serialize (JObject m))

let getMetadata (script: ScriptInfo) : Map<string, JsonValue> =
    match loadMetadata () |> Map.tryFind script.Path with
    | Some(JObject fields) -> fields
    | _ -> Map.empty

/// Merge new fields into a script's metadata and stamp last_updated.
let updateMetadata (script: ScriptInfo) (fields: (string * JsonValue) list) : unit =
    let all = loadMetadata ()
    let existing = all |> Map.tryFind script.Path |> Option.defaultValue (JObject Map.empty)

    let merged =
        (existing, fields)
        ||> List.fold (fun acc (k, v) -> JsonValue.setPath [ k ] v acc)
        |> JsonValue.setPath [ "last_updated" ] (JString(DateTime.UtcNow.ToString "o"))

    saveMetadata (all |> Map.add script.Path merged)

let removeMetadata (script: ScriptInfo) : unit =
    saveMetadata (loadMetadata () |> Map.remove script.Path)

// --- Templates ------------------------------------------------------------------

let private shellBasicTemplate =
    """#!/usr/bin/env bash
set -euo pipefail

# TODO: implement your script.
echo "Hello from $(basename "$0")"
"""

let private pythonBasicTemplate =
    """#!/usr/bin/env python3
import sys


def main(args: list[str]) -> int:
    print(f"Hello from {sys.argv[0]}: {args}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
"""

let private builtinTemplates: Map<string * string, string> =
    Map.ofList [ ("shell", "basic"), shellBasicTemplate; ("python", "basic"), pythonBasicTemplate ]

let private userTemplatesDir () : string option =
    let fromEnv = Environment.GetEnvironmentVariable "SKOGAI_TEMPLATES_DIR"

    let fromSetting =
        match Settings.get "script.templates_dir" with
        | Some(JString s) -> s
        | _ -> null

    [ fromEnv; fromSetting ] |> List.tryFind (fun p -> not (String.IsNullOrWhiteSpace p) && Directory.Exists p)

let private extensionFor (scriptType: string) = if scriptType = "python" then ".py" else ".sh"

/// Resolve template content: user template directory first, then built-ins.
let getTemplateContent (templateName: string) (scriptType: string) : Result<string, string> =
    let ext = extensionFor scriptType

    let fromUserDir =
        userTemplatesDir ()
        |> Option.bind (fun dir ->
            let p = Path.Combine(dir, scriptType, $"{templateName}{ext}")
            if File.Exists p then Some(File.ReadAllText p) else None)

    match fromUserDir with
    | Some content -> Ok content
    | None ->
        match builtinTemplates |> Map.tryFind (scriptType, templateName) with
        | Some content -> Ok content
        | None -> Error $"Template '{templateName}' for {scriptType} not found"

/// All available templates, grouped by script type.
let listAvailableTemplates () : Map<string, string list> =
    let builtin =
        builtinTemplates
        |> Map.toList
        |> List.map fst
        |> List.groupBy fst
        |> List.map (fun (t, names) -> t, names |> List.map snd)
        |> Map.ofList

    match userTemplatesDir () with
    | None -> builtin
    | Some dir ->
        Directory.GetDirectories dir
        |> Array.fold
            (fun acc typeDir ->
                let typeName = Path.GetFileName typeDir

                let names =
                    Directory.GetFiles typeDir |> Array.map Path.GetFileNameWithoutExtension |> List.ofArray

                let existing = acc |> Map.tryFind typeName |> Option.defaultValue []
                acc |> Map.add typeName (existing @ names |> List.distinct))
            builtin

// --- Commands ---------------------------------------------------------------------

type CreateResult =
    | Created of ScriptInfo
    | AlreadyExists of ScriptInfo
    | TemplateMissing of string

let createScript
    (name: string)
    (scriptType: string)
    (templateName: string)
    (asGlobal: bool)
    (description: string)
    : CreateResult =
    let dir = if asGlobal then Directory.CreateDirectory(Paths.globalScriptsDir ()).FullName else Paths.userScriptsDir ()
    let ext = extensionFor scriptType
    let path = Path.Combine(dir, $"{name}{ext}")
    let info =
        { Path = path
          Name = name
          Kind = (if scriptType = "python" then Python else Shell)
          Location = (if asGlobal then GlobalScript else UserScript) }

    if File.Exists path then
        AlreadyExists info
    else
        match getTemplateContent templateName scriptType with
        | Error e -> TemplateMissing e
        | Ok content ->
            File.WriteAllText(path, content)
            makeExecutable path

            updateMetadata
                info
                [ "description", JString description
                  "template", JString templateName
                  "type", JString scriptType
                  "created", JString(DateTime.UtcNow.ToString "o")
                  "run_count", JNumber 0.0 ]

            Created info

let removeScript (script: ScriptInfo) : unit =
    File.Delete script.Path
    removeMetadata script

type RunOutcome =
    { ExitCode: int
      Stdout: string
      Stderr: string }

/// Run a script with the given arguments. Shell scripts and other executables
/// run directly; Python scripts run under `python3` for portability.
let runScript (script: ScriptInfo) (args: string list) : RunOutcome =
    let meta = getMetadata script
    let runCount =
        match meta |> Map.tryFind "run_count" with
        | Some(JNumber n) -> n + 1.0
        | _ -> 1.0

    updateMetadata script [ "run_count", JNumber runCount; "last_run", JString(DateTime.UtcNow.ToString "o") ]

    if not (isExecutable script.Path) then
        makeExecutable script.Path

    let result =
        match script.Kind with
        | Python -> Proc.run "python3" (script.Path :: args)
        | _ -> Proc.run script.Path args

    { ExitCode = result.ExitCode
      Stdout = result.Stdout
      Stderr = result.Stderr }

let copyScript (source: ScriptInfo) (destName: string) (asGlobal: bool) : Result<ScriptInfo, string> =
    let dir = if asGlobal then Directory.CreateDirectory(Paths.globalScriptsDir ()).FullName else Paths.userScriptsDir ()
    let ext = Path.GetExtension source.Path
    let destPath = Path.Combine(dir, $"{destName}{ext}")

    if File.Exists destPath then
        Error $"Script '{destName}' already exists."
    else
        File.Copy(source.Path, destPath)
        makeExecutable destPath

        let dest =
            { Path = destPath
              Name = destName
              Kind = kindOf destPath
              Location = (if asGlobal then GlobalScript else UserScript) }

        let sourceMeta = getMetadata source

        if not sourceMeta.IsEmpty then
            let fields =
                sourceMeta
                |> Map.remove "last_run"
                |> Map.remove "last_edited"
                |> Map.add "copied_from" (JString source.Path)
                |> Map.add "created" (JString(DateTime.UtcNow.ToString "o"))
                |> Map.add "run_count" (JNumber 0.0)
                |> Map.toList

            updateMetadata dest fields

        Ok dest

type SearchMatch = { LineNumber: int; Line: string }
type SearchResult = { Script: ScriptInfo; Matches: SearchMatch list }

/// Search script contents for a literal substring or regular expression.
let searchScripts (pattern: string) (useRegex: bool) (caseSensitive: bool) (includeGlobal: bool) : SearchResult list =
    let isMatch =
        if useRegex then
            let options =
                if caseSensitive then
                    Text.RegularExpressions.RegexOptions.None
                else
                    Text.RegularExpressions.RegexOptions.IgnoreCase

            let re = Text.RegularExpressions.Regex(pattern, options)
            fun (line: string) -> re.IsMatch line
        else if caseSensitive then
            fun (line: string) -> line.Contains(pattern: string)
        else
            let needle = pattern.ToLowerInvariant()
            fun (line: string) -> line.ToLowerInvariant().Contains needle

    listScripts includeGlobal
    |> List.choose (fun script ->
        try
            let matches =
                File.ReadAllLines script.Path
                |> Array.mapi (fun i line -> i + 1, line)
                |> Array.filter (fun (_, line) -> isMatch line)
                |> Array.map (fun (n, line) -> { LineNumber = n; Line = line.Trim() })
                |> List.ofArray

            if matches.IsEmpty then None else Some { Script = script; Matches = matches }
        with _ ->
            None)
