---
set: stable-dependency-rules
namespace: ddd-analyzers
source-draft: ADR-A04
decisions:
  - key: LayerReferenceStrictlyDownward
    statement: "DD0001: a project with ArchLayer n may reference a family assembly only if that assembly declares a lower layer; an undeclared family reference is an error"
  - key: LayerDeclaredInAssemblyMetadata
    statement: "The layer is read from a source-generated assembly-level ArchLayer attribute so the check works across project and package references"
  - key: InternalsVisibleToTestsOnly
    statement: "DD0002: every InternalsVisibleTo target ends in .Tests"
  - key: NoServiceLocationOutsideCompositionRoot
    statement: "DD0003: IServiceProvider resolution, ActivatorUtilities and Activator.CreateInstance are errors unless the project declares ArchCompositionRoot=true"
  - key: CallbackLoopholeNeedsNoRule
    statement: "A lower layer cannot type a callback to a higher-layer type without the reference DD0001 forbids; object-typed contract parameters are closed by DD0011 and dynamic by banned symbols"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A04-*.md.
