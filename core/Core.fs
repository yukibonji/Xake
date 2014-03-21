﻿namespace Xake

[<AutoOpen>]
module Core =

  open System.IO
  open System.Threading.Tasks

  open Xake

  // execution context
  type ExecMessage =
    | Run of ArtifactType * BuildActionType * AsyncReplyChannel<Task<FileInfo>>
    | Reset
    | GetTask of FileInfo * AsyncReplyChannel<Task<FileInfo> option>

  let private fileinfo artifact =
    let (FileArtifact file) = artifact
    file

  // gets the artifact file name
  let fullname (FileArtifact file) = file.FullName

  let execstate = MailboxProcessor.Start(fun mbox ->
    let rec loop(map) = async {
      let! msg = mbox.Receive()
      match msg with
      | Reset ->        
        // TODO cancel pending tasks
        // map |> Map.iter (fun file task -> ...)
        return! loop(Map.empty)

      | Run(artifact, BuildAction action,chnl) ->
        
        let fullname = fullname artifact
        let task = map |> Map.tryFind fullname

        match task with
        | Some task -> 
          chnl.Reply(task)
          return! loop(map)

        | None ->
          let task = Async.StartAsTask (async {
            do! action artifact
            do log Level.Verbose "completed build '%s'" fullname
            return (fileinfo artifact)
          })

          do log Level.Verbose "started build '%s'" fullname
          chnl.Reply(task)
          return! loop(Map.add fullname task map)

      | GetTask(file,chnl) ->        
        chnl.Reply (map |> Map.tryFind file.FullName)
        return! loop(map)
    }
    loop(Map.empty) )

  // changes file extension
  let (-<.>) (file:FileInfo) newExt = Path.ChangeExtension(file.FullName,newExt)

  // turns the file into FileArtifact type
  [<System.Obsolete>]
  let simplefile = FileArtifact

  let mutable private rules = Map.empty

  // locates the rule
  let internal locateRule artifact : Async<unit> option =

    // tests if file name matches
    let globToRegex (mask: string) =
      let c = function
        | '*' -> ".+"
        | '.' -> "[.]"
        | '?' -> "."
        | ch -> System.String(ch,1)
      (mask.ToCharArray() |> Array.map c |> System.String.Concat) + "$"

    // TODO locate non-file rules
    let (FileArtifact file) = artifact

    let regexpMatch pat =
      System.Text.RegularExpressions.Regex.Matches(file.FullName, pat).Count > 0

    let rulename = function
      | Regexp pat -> "regexp " + pat
      | Glob glob -> "glob " + glob
      | Name name -> "name " + name

    let matchRule rule a =
      let matched =
        match rule with
        | Regexp pat -> regexpMatch pat
        | Glob glob -> regexpMatch (globToRegex glob)
        | Name name -> name.Equals(file.Name, System.StringComparison.OrdinalIgnoreCase)
      match matched with
      | true ->
        log Verbose "Found rule '%s' for %s" (rulename rule) file.Name
        Some (a)
      | false -> None
    let mapAction = function
      | BuildAction a -> a artifact
      | BuildFile b -> b file
    Map.tryPick matchRule rules |> Option.map mapAction

  let locateRuleOrDie a =
    match locateRule a with
    | Some rule -> rule
    | None -> failwithf "Failed to locate file for '%s'" (fullname a)

  // executes single artifact
  let private execOne artifact =
    match locateRule artifact with
    | Some rule ->
      async {      
        let! task = execstate.PostAndAsyncReply(fun chnl -> Run(artifact, BuildAction (fun _ -> rule), chnl))
        return! Async.AwaitTask task
      }
    | None ->
      if not (fileinfo artifact).Exists then failwithf "Neither rule nor file is found for '%s'" (fullname artifact)
      Async.FromContinuations (fun (cont,_e,_c) -> cont(fileinfo artifact))

  let exec = Seq.ofList >> Seq.map execOne >> Async.Parallel
  let need artifacts = artifacts |> exec |> Async.Ignore

  // Runs the artifact synchronously
  let runSync a =
    locateRuleOrDie a |> Async.RunSynchronously |> ignore
    fileinfo a

  // runs execution of all artifact rules in parallel
  let run =
    List.map locateRule >> List.filter Option.isSome >> List.map Option.get >> Seq.ofList >> Async.Parallel >> Async.RunSynchronously >> ignore

  // creates new artifact rule
  let ( ***> ) selector action : unit =
    let rule = Rule (selector, action)
    rules <- Map.add selector action rules

  let ( **> ) selector buildfile : unit =
    let rule = Rule (selector, BuildFile buildfile)
    rules <- Map.add selector (BuildFile buildfile) rules

  // gets an rule for file
  let ( ~& ) path = FileArtifact (System.IO.FileInfo path)
  let ( ~&& ) path = FileArtifact (System.IO.FileInfo path)

  let rule = async

  // TODO move all three generic methods somewhere else Xake.Util?
  // generic method to turn any fn to async task
  let wait fn artifact = async {
    do! need [artifact]
    return artifact |> fn
    }

  // executes the fn on all artifacts
  let allf fn aa = async {
    let! results = aa |> (List.map fn) |> List.toSeq |> Async.Parallel
    return results |> Array.toList
    }

  let all aa =
    let identity i = i
    allf identity aa