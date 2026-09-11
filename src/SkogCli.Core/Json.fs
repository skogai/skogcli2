/// A small, statically typed JSON value model used for SkogCli's dynamic
/// configuration trees (agent configs, settings, chat history, ...).
///
/// The Python original passed raw `dict[str, Any]` around everywhere; here every
/// config value is a `JsonValue`, so callers must explicitly handle every shape
/// instead of hoping a key holds the type they expect.
module SkogCli.Core.Json

open System
open System.Text
open System.Text.Json

type JsonValue =
    | JString of string
    | JNumber of float
    | JBool of bool
    | JNull
    | JArray of JsonValue list
    | JObject of Map<string, JsonValue>

module JsonValue =

    let ofString (s: string) = JString s
    let ofBool (b: bool) = JBool b
    let ofInt (i: int) = JNumber(float i)
    let ofFloat (f: float) = JNumber f

    let rec private ofElement (element: JsonElement) : JsonValue =
        match element.ValueKind with
        | JsonValueKind.String -> JString(element.GetString())
        | JsonValueKind.Number -> JNumber(element.GetDouble())
        | JsonValueKind.True -> JBool true
        | JsonValueKind.False -> JBool false
        | JsonValueKind.Array ->
            element.EnumerateArray() |> Seq.map ofElement |> List.ofSeq |> JArray
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.map (fun p -> p.Name, ofElement p.Value)
            |> Map.ofSeq
            |> JObject
        | _ -> JNull

    /// Parse a JSON document into a JsonValue. Returns Error with a message on malformed input.
    let parse (text: string) : Result<JsonValue, string> =
        try
            use doc = JsonDocument.Parse(text)
            Ok(ofElement doc.RootElement)
        with :? JsonException as ex ->
            Error ex.Message

    let private escape (s: string) =
        let sb = StringBuilder(s.Length + 2)
        for c in s do
            match c with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore
        sb.ToString()

    let private numberText (n: float) =
        if Double.IsNaN n || Double.IsInfinity n then "null"
        elif Double.IsInteger n && abs n < 1e15 then string (int64 n)
        else n.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    /// Serialize a JsonValue as JSON text. `indent = None` produces compact output.
    let rec private write (sb: StringBuilder) (indent: int option) (depth: int) (v: JsonValue) =
        let newlineIndent d =
            match indent with
            | Some n -> sb.Append('\n').Append(' ', n * d) |> ignore
            | None -> ()

        match v with
        | JNull -> sb.Append("null") |> ignore
        | JBool b -> sb.Append(if b then "true" else "false") |> ignore
        | JNumber n -> sb.Append(numberText n) |> ignore
        | JString s -> sb.Append('"').Append(escape s).Append('"') |> ignore
        | JArray [] -> sb.Append("[]") |> ignore
        | JArray items ->
            sb.Append('[') |> ignore
            items
            |> List.iteri (fun i item ->
                if i > 0 then
                    sb.Append(',') |> ignore

                newlineIndent (depth + 1)
                write sb indent (depth + 1) item)
            newlineIndent depth
            sb.Append(']') |> ignore
        | JObject fields when fields.IsEmpty -> sb.Append("{}") |> ignore
        | JObject fields ->
            sb.Append('{') |> ignore
            fields
            |> Map.toList
            |> List.iteri (fun i (k, value) ->
                if i > 0 then
                    sb.Append(',') |> ignore

                newlineIndent (depth + 1)
                sb.Append('"').Append(escape k).Append("\":") |> ignore
                if indent.IsSome then sb.Append(' ') |> ignore
                write sb indent (depth + 1) value)
            newlineIndent depth
            sb.Append('}') |> ignore

    /// Serialize a JsonValue as indented (2-space) JSON text.
    let serialize (v: JsonValue) : string =
        let sb = StringBuilder()
        write sb (Some 2) 0 v
        sb.ToString()

    /// Serialize compactly (no whitespace).
    let serializeCompact (v: JsonValue) : string =
        let sb = StringBuilder()
        write sb None 0 v
        sb.ToString()

    /// Render a scalar value the way a human would type it back in (no quotes on strings).
    let display (v: JsonValue) : string =
        match v with
        | JString s -> s
        | JBool b -> if b then "True" else "False"
        | JNull -> "None"
        | JNumber n -> if Double.IsInteger n then string (int64 n) else string n
        | JArray _
        | JObject _ -> serialize v

    let isContainer =
        function
        | JObject _
        | JArray _ -> true
        | _ -> false

    /// Look up a dotted path ("agent.coder.model") inside a JsonValue tree.
    let rec getPath (path: string list) (v: JsonValue) : JsonValue option =
        match path, v with
        | [], _ -> Some v
        | key :: rest, JObject fields -> fields |> Map.tryFind key |> Option.bind (getPath rest)
        | _ -> None

    /// Set a dotted path inside a JsonValue tree, creating intermediate objects as needed.
    /// Non-object nodes encountered along the path are overwritten with fresh objects.
    let rec setPath (path: string list) (value: JsonValue) (v: JsonValue) : JsonValue =
        match path with
        | [] -> value
        | key :: rest ->
            let fields =
                match v with
                | JObject f -> f
                | _ -> Map.empty

            let existing = fields |> Map.tryFind key |> Option.defaultValue (JObject Map.empty)
            let updated = setPath rest value existing
            JObject(fields |> Map.add key updated)

    /// Remove a dotted path from a JsonValue tree. No-op if the path doesn't exist.
    let rec removePath (path: string list) (v: JsonValue) : JsonValue =
        match path, v with
        | [], _ -> v
        | [ key ], JObject fields -> JObject(fields |> Map.remove key)
        | key :: rest, JObject fields ->
            match fields |> Map.tryFind key with
            | Some child -> JObject(fields |> Map.add key (removePath rest child))
            | None -> v
        | _ -> v

    /// Flatten an object tree into dotted key/value pairs (leaves only), mirroring the
    /// Python CLI's "config list" behaviour.
    let rec flatten (prefix: string) (v: JsonValue) : (string * JsonValue) list =
        match v with
        | JObject fields when not fields.IsEmpty ->
            fields
            |> Map.toList
            |> List.collect (fun (k, value) ->
                let fullKey = if prefix = "" then k else $"{prefix}.{k}"
                flatten fullKey value)
        | _ -> [ (prefix, v) ]

    /// Parse a raw CLI string the way the Python CLI infers types for "config set":
    /// JSON first (objects/arrays/numbers/bools/null), then plain string as a fallback.
    let parseValueLoosely (raw: string) : JsonValue =
        match parse raw with
        | Ok v -> v
        | Error _ -> JString raw
