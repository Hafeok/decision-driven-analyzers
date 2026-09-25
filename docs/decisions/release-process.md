---
set: release-process
namespace: ddd-analyzers
decisions:
  - key: PublishedBytesAreTestedBytes
    statement: "The packages pushed to nuget.org are the artifacts the build workflow uploaded in the same run, after every check in it passed on those bytes; publishing never rebuilds or repacks"
  - key: TagPublishesOnlyThroughGate
    statement: "A v* tag publishes only after the full check set of build.yml passes in the same run and the nuget environment approval is given; a tag is a request to publish, not a publish"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. No draft: the maintainer's call of 2026-09-25, after `0.1.0-preview.2` was published by a tag push that repacked from source, never ran the samples job on the bytes it pushed, and finished while CI on the same commit was still running.

`.github/workflows/build.yml` is the check set: build, test and pack on Linux and Windows, the samples job against the packed nupkgs, and the format check. `ci.yml` calls it for every push and pull request. `publish.yml` calls it as its first job, and its push job downloads the `packages` artifact that run uploaded and pushes those files.
