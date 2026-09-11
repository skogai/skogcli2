module SkogCli.Tests.Tests

open System
open Xunit
open SkogCli.Core.Json

// ---------------------------------------------------------------- Json ---

[<Fact>]
let ``parse and serialize round-trips an object`` () =
    let json = """{"a": 1, "b": [true, null, "x"], "c": {"d": 2.5}}"""

    match JsonValue.parse json with
    | Error e -> failwith e
    | Ok v ->
        Assert.Equal(Some(JNumber 1.0), JsonValue.getPath [ "a" ] v)
        Assert.Equal(Some(JNumber 2.5), JsonValue.getPath [ "c"; "d" ] v)

        match JsonValue.parse (JsonValue.serialize v) with
        | Error e -> failwith e
        | Ok roundTripped -> Assert.Equal<JsonValue>(v, roundTripped)

[<Fact>]
let ``setPath creates intermediate objects`` () =
    let result = JsonValue.setPath [ "agent"; "coder"; "model" ] (JString "gpt-5") (JObject Map.empty)
    Assert.Equal(Some(JString "gpt-5"), JsonValue.getPath [ "agent"; "coder"; "model" ] result)

[<Fact>]
let ``setPath overwrites a non-object node along the path`` () =
    let start = JObject(Map.ofList [ "agent", JString "not-an-object" ])
    let result = JsonValue.setPath [ "agent"; "model" ] (JString "gpt-5") start
    Assert.Equal(Some(JString "gpt-5"), JsonValue.getPath [ "agent"; "model" ] result)

[<Fact>]
let ``removePath deletes a leaf without disturbing siblings`` () =
    let start =
        JObject(Map.ofList [ "agent", JObject(Map.ofList [ "a", JString "1"; "b", JString "2" ]) ])

    let result = JsonValue.removePath [ "agent"; "a" ] start
    Assert.Equal(None, JsonValue.getPath [ "agent"; "a" ] result)
    Assert.Equal(Some(JString "2"), JsonValue.getPath [ "agent"; "b" ] result)

[<Fact>]
let ``flatten produces dotted leaf keys`` () =
    let tree =
        JObject(Map.ofList [ "a", JNumber 1.0; "b", JObject(Map.ofList [ "c", JNumber 2.0; "d", JNumber 3.0 ]) ])

    let flat = JsonValue.flatten "" tree |> List.sortBy fst
    Assert.Equal<(string * JsonValue) list>([ "a", JNumber 1.0; "b.c", JNumber 2.0; "b.d", JNumber 3.0 ], flat)

[<Theory>]
[<InlineData("42", 42.0)>]
[<InlineData("3.5", 3.5)>]
let ``parseValueLoosely recognises numbers`` (input: string) (expected: float) =
    Assert.Equal(JNumber expected, JsonValue.parseValueLoosely input)

[<Fact>]
let ``parseValueLoosely recognises booleans and null via JSON`` () =
    Assert.Equal(JBool true, JsonValue.parseValueLoosely "true")
    Assert.Equal(JNull, JsonValue.parseValueLoosely "null")

[<Fact>]
let ``parseValueLoosely falls back to a plain string for non-JSON text`` () =
    Assert.Equal(JString "hello world", JsonValue.parseValueLoosely "hello world")

[<Fact>]
let ``parseValueLoosely recognises JSON objects and arrays`` () =
    Assert.Equal(JArray [ JNumber 1.0; JNumber 2.0 ], JsonValue.parseValueLoosely "[1, 2]")

// ------------------------------------------------------------- Settings ---

/// Settings/Paths read the SKOGAI_CONFIG_DIR env var on every call, so each
/// test gets its own throwaway directory - this module's tests can't safely
/// run in parallel with each other (see AssemblyInfo.fs), but are isolated
/// from anything on the real filesystem or from a developer's own config.
let private withTempConfigDir (f: unit -> unit) =
    let dir = IO.Path.Combine(IO.Path.GetTempPath(), "skogcli-tests-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("SKOGAI_CONFIG_DIR", dir)

    try
        f ()
    finally
        Environment.SetEnvironmentVariable("SKOGAI_CONFIG_DIR", null)
        IO.Directory.Delete(dir, recursive = true)

[<Fact>]
let ``get and set round-trip a dotted key`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "agent.coder.model" (JString "gpt-5")
        Assert.Equal(Some(JString "gpt-5"), SkogCli.Core.Settings.get "agent.coder.model"))

[<Fact>]
let ``get returns None for a missing key`` () =
    withTempConfigDir (fun () -> Assert.Equal(None, SkogCli.Core.Settings.get "nope.not.here"))

[<Fact>]
let ``credentials are stored separately from config json`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "credentials.api_key" (JString "secret")
        Assert.Equal(Some(JString "secret"), SkogCli.Core.Settings.get "credentials.api_key")

        let onDisk = IO.File.ReadAllText(SkogCli.Core.Paths.configFile ())
        Assert.DoesNotContain("secret", onDisk))

[<Fact>]
let ``setting a second credential does not clobber a previously stored one`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "credentials.api_key" (JString "secret1")
        SkogCli.Core.Settings.set "credentials.other_key" (JString "secret2")

        Assert.Equal(Some(JString "secret1"), SkogCli.Core.Settings.get "credentials.api_key")
        Assert.Equal(Some(JString "secret2"), SkogCli.Core.Settings.get "credentials.other_key"))

[<Fact>]
let ``an environment override takes precedence over a stored value`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "agent.default_model" (JString "stored")
        Environment.SetEnvironmentVariable("SKOGAI_AGENT_DEFAULT_MODEL", "from-env")

        try
            Assert.Equal(Some(JString "from-env"), SkogCli.Core.Settings.get "agent.default_model")
        finally
            Environment.SetEnvironmentVariable("SKOGAI_AGENT_DEFAULT_MODEL", null))

[<Fact>]
let ``reset restores default settings`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "agent.default_model" (JString "custom")
        SkogCli.Core.Settings.reset ()
        Assert.Equal(None, SkogCli.Core.Settings.get "agent.default_model"))

[<Fact>]
let ``history is capped and returned newest first`` () =
    withTempConfigDir (fun () ->
        SkogCli.Core.Settings.set "settings.module.max_history_items" (JNumber 2.0)

        for i in 1..3 do
            SkogCli.Core.Settings.addHistoryItem (
                JObject(Map.ofList [ "message", JString(string i); "timestamp", JString(DateTime(2020, 1, i).ToString "o") ])
            )

        let history = SkogCli.Core.Settings.getHistory None
        Assert.Equal(2, history.Length)
        Assert.Equal(Some(JString "3"), JsonValue.getPath [ "message" ] history.[0]))

// --------------------------------------------------------------- Scripts ---

open SkogCli.Scripts.Scripts

let private withTempScriptsDir (f: unit -> unit) =
    let dir = IO.Path.Combine(IO.Path.GetTempPath(), "skogcli-scripts-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("SKOGAI_SCRIPTS_DIR", dir)
    Environment.SetEnvironmentVariable("SKOGAI_SCRIPTS_GLOBAL_DIR", IO.Path.Combine(dir, "global"))
    let metaDir = IO.Path.Combine(IO.Path.GetTempPath(), "skogcli-scripts-meta-" + Guid.NewGuid().ToString "N")
    Environment.SetEnvironmentVariable("SKOGAI_SCRIPT_METADATA_DIR", metaDir)

    try
        f ()
    finally
        Environment.SetEnvironmentVariable("SKOGAI_SCRIPTS_DIR", null)
        Environment.SetEnvironmentVariable("SKOGAI_SCRIPTS_GLOBAL_DIR", null)
        Environment.SetEnvironmentVariable("SKOGAI_SCRIPT_METADATA_DIR", null)
        if IO.Directory.Exists dir then IO.Directory.Delete(dir, recursive = true)
        if IO.Directory.Exists metaDir then IO.Directory.Delete(metaDir, recursive = true)

[<Fact>]
let ``createScript writes an executable file from the basic template`` () =
    withTempScriptsDir (fun () ->
        match createScript "hello" "shell" "basic" false "a test script" with
        | Created info ->
            Assert.True(IO.File.Exists info.Path)
            Assert.Equal(Shell, info.Kind)
            Assert.Equal(UserScript, info.Location)
        | other -> failwith $"expected Created, got %A{other}")

[<Fact>]
let ``createScript reports AlreadyExists on a second call with the same name`` () =
    withTempScriptsDir (fun () ->
        createScript "dup" "shell" "basic" false "" |> ignore

        match createScript "dup" "shell" "basic" false "" with
        | AlreadyExists _ -> ()
        | other -> failwith $"expected AlreadyExists, got %A{other}")

[<Fact>]
let ``findScript locates a script by bare name`` () =
    withTempScriptsDir (fun () ->
        createScript "findme" "shell" "basic" false "" |> ignore
        Assert.True((findScript "findme" true).IsSome)
        Assert.True((findScript "missing" true).IsNone))

[<Theory>]
[<InlineData("../escape")>]
[<InlineData("a/b")>]
[<InlineData("a\\b")>]
[<InlineData("..")>]
let ``createScript rejects names that could escape the scripts directory`` (name: string) =
    withTempScriptsDir (fun () ->
        match createScript name "shell" "basic" false "" with
        | InvalidName _ -> ()
        | other -> failwith $"expected InvalidName for '%s{name}', got %A{other}")

[<Fact>]
let ``copyScript rejects a destination name that could escape the scripts directory`` () =
    withTempScriptsDir (fun () ->
        match createScript "source" "shell" "basic" false "" with
        | Created info ->
            match copyScript info "../escape" false with
            | Error _ -> ()
            | Ok dest -> failwith $"expected an error, got %A{dest}"
        | other -> failwith $"expected Created, got %A{other}")

[<Fact>]
let ``runScript executes the script and tracks run_count`` () =
    withTempScriptsDir (fun () ->
        match createScript "runnable" "shell" "basic" false "" with
        | Created info ->
            let outcome = runScript info []
            Assert.Equal(0, outcome.ExitCode)
            Assert.Contains("Hello from", outcome.Stdout)

            let meta = getMetadata info
            Assert.Equal(Some(JNumber 1.0), meta |> Map.tryFind "run_count")
        | other -> failwith $"expected Created, got %A{other}")

[<Fact>]
let ``removeScript deletes the file and its metadata`` () =
    withTempScriptsDir (fun () ->
        match createScript "removable" "shell" "basic" false "" with
        | Created info ->
            removeScript info
            Assert.False(IO.File.Exists info.Path)
            Assert.True((getMetadata info).IsEmpty)
        | other -> failwith $"expected Created, got %A{other}")

[<Fact>]
let ``copyScript duplicates content and resets run count`` () =
    withTempScriptsDir (fun () ->
        match createScript "original" "shell" "basic" false "" with
        | Created source ->
            runScript source [] |> ignore

            match copyScript source "copy" false with
            | Ok dest ->
                Assert.True(IO.File.Exists dest.Path)
                let meta = getMetadata dest
                Assert.Equal(Some(JNumber 0.0), meta |> Map.tryFind "run_count")
                Assert.Equal(Some(JString source.Path), meta |> Map.tryFind "copied_from")
            | Error e -> failwith e
        | other -> failwith $"expected Created, got %A{other}")

[<Fact>]
let ``searchScripts finds matching lines`` () =
    withTempScriptsDir (fun () ->
        createScript "searchable" "shell" "basic" false "" |> ignore
        let results = searchScripts "Hello from" false false true
        Assert.Single(results) |> ignore
        Assert.True(results.[0].Matches.Length >= 1))
