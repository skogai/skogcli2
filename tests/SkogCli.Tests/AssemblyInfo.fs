// Settings/Scripts tests point at the real environment (SKOGAI_CONFIG_DIR,
// SKOGAI_SCRIPTS_DIR, ...) via process-wide env vars, so tests must not run
// concurrently with each other or they'll clobber one another's directories.
module SkogCli.Tests.AssemblyInfo

open Xunit

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()
