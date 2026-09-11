/// Helper for running external processes and capturing their output, used to
/// shell out to `basic-memory`, editors, and user scripts.
module SkogCli.Core.Proc

open System.Diagnostics

type ProcResult =
    { ExitCode: int
      Stdout: string
      Stderr: string }

/// Run `command arg1 arg2 ...` and capture stdout/stderr. Never throws on a
/// non-zero exit code; callers inspect ExitCode.
let run (command: string) (args: string list) : ProcResult =
    let psi = ProcessStartInfo(command)
    for a in args do
        psi.ArgumentList.Add a

    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false

    try
        use p = Process.Start psi
        let stdout = p.StandardOutput.ReadToEnd()
        let stderr = p.StandardError.ReadToEnd()
        p.WaitForExit()

        { ExitCode = p.ExitCode
          Stdout = stdout
          Stderr = stderr }
    with :? System.ComponentModel.Win32Exception as ex ->
        { ExitCode = 127
          Stdout = ""
          Stderr = $"Command not found: {command} ({ex.Message})" }

/// Run a process with inherited stdio (so interactive tools like editors work),
/// returning just the exit code.
let runInteractive (command: string) (args: string list) : int =
    let psi = ProcessStartInfo(command)
    for a in args do
        psi.ArgumentList.Add a

    psi.UseShellExecute <- false

    try
        use p = Process.Start psi
        p.WaitForExit()
        p.ExitCode
    with :? System.ComponentModel.Win32Exception ->
        127
