/// Filesystem locations used by SkogCli.
///
/// The Python original resolved its config directory by reading a setting that
/// itself required loading the config file from... the config directory - a
/// circular dependency that only worked because `SKOGAI_CONFIG_DIR` happened to
/// always be set in practice. Here every directory has a well-defined, acyclic
/// fallback so the CLI works out of the box.
module SkogCli.Core.Paths

open System
open System.IO

let private xdgConfigHome () =
    match Environment.GetEnvironmentVariable "XDG_CONFIG_HOME" with
    | v when not (String.IsNullOrWhiteSpace v) -> v
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".config")

let private xdgDataHome () =
    match Environment.GetEnvironmentVariable "XDG_DATA_HOME" with
    | v when not (String.IsNullOrWhiteSpace v) -> v
    | _ -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".local", "share")

let private ensureDir (dir: string) =
    Directory.CreateDirectory(dir) |> ignore
    dir

/// The directory holding config.json / credentials.json / backups.
/// Override with SKOGAI_CONFIG_DIR.
let configDir () : string =
    match Environment.GetEnvironmentVariable "SKOGAI_CONFIG_DIR" with
    | v when not (String.IsNullOrWhiteSpace v) -> ensureDir v
    | _ -> ensureDir (Path.Combine(xdgConfigHome (), "skogcli"))

let backupDir () : string = ensureDir (Path.Combine(configDir (), "backups"))
let configFile () : string = Path.Combine(configDir (), "config.json")
let credentialsFile () : string = Path.Combine(configDir (), "credentials.json")

/// Directory for user-created scripts. Override with SKOGAI_SCRIPTS_DIR.
let userScriptsDir () : string =
    match Environment.GetEnvironmentVariable "SKOGAI_SCRIPTS_DIR" with
    | v when not (String.IsNullOrWhiteSpace v) -> ensureDir v
    | _ -> ensureDir (Path.Combine(xdgDataHome (), "skogcli", "scripts"))

/// Directory for machine-wide scripts shared by all users. Override with SKOGAI_SCRIPTS_GLOBAL_DIR.
let globalScriptsDir () : string =
    match Environment.GetEnvironmentVariable "SKOGAI_SCRIPTS_GLOBAL_DIR" with
    | v when not (String.IsNullOrWhiteSpace v) -> v
    | _ -> "/usr/local/share/skogcli/scripts"

/// File tracking per-script metadata (description, run counts, timestamps).
let scriptMetadataFile () : string =
    match Environment.GetEnvironmentVariable "SKOGAI_SCRIPT_METADATA_DIR" with
    | v when not (String.IsNullOrWhiteSpace v) -> Path.Combine(ensureDir v, "script_metadata.json")
    | _ -> Path.Combine(ensureDir (Path.Combine(xdgDataHome (), "skogcli")), "script_metadata.json")

/// Directory for agent-invocation shell scripts (./scripts relative to the CLI's own cwd,
/// matching the Python original so existing agent scripts keep working).
let agentScriptsDir () : string = ensureDir "scripts"
