﻿# Implementation notes
## Fileset
Implementation is very similar to NAnt's one, it does support:

  * disk reference
  * parent dir reference
  * directories and directory masks
  * recurse mask (e.f. "source/**/*.c"), supports
  * file name and mask
  * both "/" and "\" as a path separator

Fileset has two major subtypes - either **set of rules** or a **file list**, also called "materialized" fileset.

### Non-existing files
Filesets are used to references both existing files (e.g. project source files) and **targets** which might not exists when fileset is "materialized". In case the file or directory name is treated as a mask, the non-existing file will be omitted from resulting file list and the build will likely fail.
The rule to resolve such inconsistency the rule without mask is added to resulting file list explicitly.

NOTE: it should depend on the context: if fileset defines source files or references "explicit rule" is ok.

## Error handling and diagnostics
Error handling assumes the following system behavior:

  * system provide screen and file log for all targets built and other system actions
  * allows to specify detailization level for screen and file separately
  * any uncaught exception in build rule leads to script failure unless FailOnError property is for particular target is set to False
(consider "deploy" target with a fallback implementation)
  * failures and stack traces are written to log
  * idea: "onfail" target, "onfail" rule setting
  * idea: dump the whole trace to the target

## Incremental build
Xake attempts to reduce build time by analyzing results of last build. The following rules are implemented:

 * if any of dependency source files are changed
 * if dependency artifact was rebuilt
 * if there no dependencies at all (e.g. "clean" task), otherwise with incremental builds it will never rebuild
 * in case action is marked with alwaysRerun
 * environment variable or script variable the script or any task requests is changed

### Option 2. Analyze last run
This option is found in "shake" project. It stores all targets and their dependencies in database. Whenever the target is requested it checks
whether it needs to be built by analyzing whether any dependencies are changed.
The benefits of this approach in comparison to option 1 are:

 * easier to implement due to more straightforward logic
 * allows to analyze the whole dependencies graph before running any target
 * ... and estimate the build time
 * does not require changes in tasks implementation ("whenNeeded")

The difference is that the decision is made early, before executing target while option 1 does execute the code and makes a decision upon request.

### Random thoughts

GetFiles will be new monadic function. Record the call in dependencies as GetFileList(), both rules and results. Track only different results.
Need is traced as before i.e. for every "need" we record the exec time and target name.

## .NET Runtime
Xake allows using both Mono and .NET frameworks explicitly by defining NETFX variable.
Default behavior is to use the framework the script is running under. E.g. if running under Mono `fsharpi` recent mono toolset will be used.

List of valid targets:
    | "net-20" | "net-2.0" | "2.0"
    | "net-30" | "net-3.0" | "3.0"
    | "net-35" | "net-3.5" | "3.5"
    | "net-40c"| "net-4.0c" | "4.0-client"
    | "net-40" | "net-4.0" | "4.0"| "4.0-full"
    | "net-45" | "net-4.5" | "4.5"| "4.5-full"

    | "mono-20" | "mono-2.0" | "2.0"
    | "mono-35" | "mono-3.5" | "3.5"
    | "mono-40" | "mono-4.0" | "4.0"
    | "mono-45" | "mono-4.5" | "4.5"

Use "2.0".."4.5" targets for mutiplatform environments (will target mono-XXX being run under mono framework).

Tasks:
  * various supported options for csc

## Variables

 * NETFX - framework version to use for compilation, resources. E.g. "2.0", "3.5", "4.0". Default: highest available on the computer


## Other

  * file names are cases sensitive now. In the future it's planned to be system-dependent
  * external libraries has to be copied to target folder. How to accomplish?
    * csc task option "copy deps to output folder"
    * manually copy (need tracking which are really needed)
    * explicit external map of deps: use both