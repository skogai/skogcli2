/// Knowledge-base management: a thin, typed wrapper around the external
/// `basic-memory` CLI (invoked via `uvx basic-memory ...`, same as the
/// Python original).
module SkogCli.Memory.Memory

open SkogCli.Core
open SkogCli.Core.Json

type MemoryResult =
    { ExitCode: int
      Stdout: string
      Stderr: string }

let private ofProc (r: Proc.ProcResult) : MemoryResult =
    { ExitCode = r.ExitCode
      Stdout = r.Stdout
      Stderr = r.Stderr }

/// Run `basic-memory` (via `uvx`) with the given arguments.
let runBasicMemory (args: string list) : MemoryResult = Proc.run "uvx" ("basic-memory" :: args) |> ofProc

let private withProject (project: string option) (args: string list) =
    match project with
    | Some p -> [ "--project"; p ] @ args
    | None -> args

/// Create or update a note. If `content` is None, callers should have already
/// read stdin and passed it in (kept out of this module to stay I/O-free).
let writeNote (title: string) (folder: string) (content: string) (tags: string option) (project: string option) : MemoryResult =
    let baseArgs = [ "tool"; "write-note"; "--title"; title; "--folder"; folder; "--content"; content ]
    let withTags = match tags with Some t -> baseArgs @ [ "--tags"; t ] | None -> baseArgs
    runBasicMemory (withProject project withTags)

let readNote (identifier: string) (page: int) (pageSize: int) (project: string option) : MemoryResult =
    let args =
        [ "tool"; "read-note"; identifier; "--page"; string page; "--page-size"; string pageSize ]

    runBasicMemory (withProject project args)

type SearchOptions =
    { Query: string
      Permalink: bool
      Title: bool
      AfterDate: string option
      BeforeDate: string option
      Page: int
      PageSize: int
      Project: string option }

let searchNotes (opts: SearchOptions) : MemoryResult =
    let args =
        [ "tool"; "search-notes"; opts.Query; "--page"; string opts.Page; "--page-size"; string opts.PageSize ]
        @ (if opts.Permalink then [ "--permalink" ] else [])
        @ (if opts.Title then [ "--title" ] else [])
        @ (opts.AfterDate |> Option.map (fun d -> [ "--after_date"; d ]) |> Option.defaultValue [])
        @ (opts.BeforeDate |> Option.map (fun d -> [ "--before_date"; d ]) |> Option.defaultValue [])

    runBasicMemory (withProject opts.Project args)

type ListOptions =
    { ActivityType: string option
      Folder: string option
      Depth: int
      Timeframe: string
      Page: int
      PageSize: int
      MaxRelated: int
      Project: string option }

let recentActivity (opts: ListOptions) : MemoryResult =
    let args =
        [ "tool"
          "recent-activity"
          "--depth"
          string opts.Depth
          "--timeframe"
          opts.Timeframe
          "--page"
          string opts.Page
          "--page-size"
          string opts.PageSize
          "--max-related"
          string opts.MaxRelated ]
        @ (opts.ActivityType |> Option.map (fun t -> [ "--type"; t ]) |> Option.defaultValue [])
        @ (opts.Folder |> Option.map (fun f -> [ "--folder"; f ]) |> Option.defaultValue [])

    runBasicMemory (withProject opts.Project args)

let sync (project: string option) (force: bool) (dryRun: bool) (verbose: bool) : MemoryResult =
    let args =
        [ "sync" ]
        @ (if force then [ "--force" ] else [])
        @ (if dryRun then [ "--dry-run" ] else [])
        @ (if verbose then [ "--verbose" ] else [])

    runBasicMemory (withProject project args)

let projectInfo (project: string option) (asJson: bool) : MemoryResult =
    let args = [ "project"; "info" ] @ (if asJson then [ "--json" ] else [])
    runBasicMemory (withProject project args)

let listProjects () : string list =
    let result = runBasicMemory [ "tool"; "list-projects"; "--format"; "json" ]

    if result.ExitCode <> 0 then
        []
    else
        match JsonValue.parse result.Stdout with
        | Ok(JObject fields) ->
            match fields |> Map.tryFind "projects" with
            | Some(JArray items) ->
                items
                |> List.choose (function
                    | JObject f ->
                        match f |> Map.tryFind "name" with
                        | Some(JString n) -> Some n
                        | _ -> None
                    | _ -> None)
            | _ -> []
        | _ -> []
