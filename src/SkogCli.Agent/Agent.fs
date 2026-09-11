/// Agent management: named AI-agent configurations, each backed by a small
/// invocation script under ./scripts/<name>.sh that SkogCli generates and calls.
module SkogCli.Agent.Agent

open System
open System.IO
open SkogCli.Core.Json
open SkogCli.Core

type AgentConfig =
    { Name: string
      Model: string option
      SystemPrompt: string option
      Description: string option
      CommandTemplate: string option }

let private configOf (name: string) : JsonValue =
    Settings.get $"agent.{name}" |> Option.defaultValue (JObject Map.empty)

let private fieldStr (key: string) (cfg: JsonValue) : string option =
    match JsonValue.getPath [ key ] cfg with
    | Some(JString s) -> Some s
    | _ -> None

let getAgentConfig (name: string) : AgentConfig =
    let cfg = configOf name

    { Name = name
      Model = fieldStr "model" cfg
      SystemPrompt = fieldStr "system_prompt" cfg
      Description = fieldStr "description" cfg
      CommandTemplate = fieldStr "command_template" cfg }

let private agentNames () : string list =
    match Settings.get "agent.agents" with
    | Some(JArray xs) -> xs |> List.choose (function JString s -> Some s | _ -> None)
    | _ -> []

let listAgentNames = agentNames

let scriptPath (name: string) : string = Path.Combine(Paths.agentScriptsDir (), $"{name}.sh")

/// Write (or overwrite) the shell script that invokes an agent. `{message}` in
/// the command template is substituted with the script's first argument ($1).
let writeAgentScript (name: string) (command: string) : string =
    let path = scriptPath name
    let commandWithParam = command.Replace("{message}", "$1")

    let content =
        $"""#!/bin/bash
# Agent script for {name}
# This file is managed by skogcli - manual changes may be overwritten

{commandWithParam}
"""

    File.WriteAllText(path, content)

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

    path

let private defaultCommandFor (name: string) : string = $"echo \"Agent {name} is responding to: {{message}}\""

type CreateAgentResult =
    | AgentCreated of AgentConfig * scriptPath: string
    | AgentAlreadyExists

let createAgent
    (name: string)
    (model: string option)
    (systemPrompt: string option)
    (description: string option)
    (commandTemplate: string option)
    : CreateAgentResult =
    if agentNames () |> List.contains name then
        AgentAlreadyExists
    else
        let fields =
            [ model |> Option.map (fun v -> "model", JString v)
              systemPrompt |> Option.map (fun v -> "system_prompt", JString v)
              description |> Option.map (fun v -> "description", JString v)
              commandTemplate |> Option.map (fun v -> "command_template", JString v) ]
            |> List.choose id

        let command = commandTemplate |> Option.defaultValue (defaultCommandFor name)
        let path = writeAgentScript name command

        Settings.set $"agent.{name}" (JObject(Map.ofList fields))
        Settings.set "agent.agents" (JArray((agentNames () @ [ name ]) |> List.map JString))

        AgentCreated(getAgentConfig name, path)

let deleteAgent (name: string) (force: bool) (confirm: unit -> bool) : Result<unit, string> =
    if not (agentNames () |> List.contains name) then
        Error $"Agent '{name}' not found."
    elif not force && not (confirm ()) then
        Error "Deletion cancelled."
    else
        Settings.set "agent.agents" (agentNames () |> List.filter ((<>) name) |> List.map JString |> JArray)
        Settings.set $"agent.{name}" JNull

        let path = scriptPath name

        if File.Exists path then
            File.Delete path

        Ok()

/// Set a single field on an agent's configuration, given either "agent.<name>.<field>"
/// or a bare field name plus an explicit agent name.
let resolveKey (key: string) (agentName: string option) : Result<string, string> =
    if key.StartsWith "agent." then
        Ok key
    else
        match agentName with
        | Some name -> Ok $"agent.{name}.{key}"
        | None -> Error "Agent name must be provided either in the key or with --agent"

type SendResult =
    { Response: string
      ExitCode: int
      Model: string }

/// Send a message to an agent by invoking its script, creating a default
/// script on the fly if none exists yet.
let send (agentName: string) (message: string) (modelOverride: string option) : SendResult =
    let cfg = getAgentConfig agentName

    let model =
        modelOverride
        |> Option.orElse cfg.Model
        |> Option.orElse (
            match Settings.get "agent.default_model" with
            | Some(JString s) -> Some s
            | _ -> None
        )
        |> Option.defaultValue "default-model"

    let path = scriptPath agentName

    let path =
        if File.Exists path then
            path
        else
            writeAgentScript agentName (defaultCommandFor agentName)

    let result = Proc.run path [ message ]

    let response =
        if result.ExitCode = 0 then
            result.Stdout
        else
            $"Error: {result.Stderr}"

    { Response = response
      ExitCode = result.ExitCode
      Model = model }
