---
set: decisions-as-types
namespace: ddd-analyzers
source-draft: ADR-A07
decisions:
  - key: LedgerIsSourceGeneratorIsReadModel
    statement: "Decisions live in the Decision Ledger; the source generator is a read model over its export and never a second source of truth"
  - key: NTriplesExportIsGeneratorInput
    statement: "The generator reads a committed N-Triples export per ledger namespace passed as AdditionalFiles; no Turtle parser and no dependencies in the generator"
  - key: InterimFrontMatterUntilExport
    statement: "Until the ledger format carries keys and decision-cli exports, a markdown set file with a decisions list maps one-to-one onto the same model and is deleted once imported"
  - key: OneTypePerSetNestedTypePerDecision
    statement: "The generator emits one static class per set (tip-version membership) with one nested static class per decision named by its key, in namespace DecisionDriven.Ledger.<Namespace> where the ledger namespace is PascalCased the same way a set id is"
  - key: VersionLevelKeyCarriedAcrossSupersession
    statement: "A decision key is a hashed version-level field unique per (namespace, key), immutable across versions, carried to the successor on supersession, syntax ^[A-Z][A-Za-z0-9]{0,63}$"
  - key: SupersessionIsNotObsolescence
    statement: "Superseded decisions emit no Obsolete; the diverging cited version is the invalidation signal"
  - key: UnacceptedEmitsWarningObsolete
    statement: "A decision whose tip version has no unrevoked acceptance is emitted with Obsolete(error=false), so it cannot ship under TreatWarningsAsErrors"
  - key: RevokedEmitsErrorObsolete
    statement: "A revoked decision without successor is emitted with Obsolete(error=true); the ledger has no decision-level retirement yet, so until it does this is fed only by the interim front matter's revoked-at"
  - key: CitationVersionDerivedNotWritten
    statement: "The version a citation was written against is never written in source; the report tool derives it from the introducing commit and the ledger tip at that commit"
  - key: AttributesAreSourceGenerated
    statement: "Contract, DomainModel, HotPath, DesignDecision, ArchLayer and ExceptionScope are emitted as internal types into each consuming compilation and matched by full name"
  - key: AttributesTakeOneDecisionType
    statement: "Contract, DomainModel, HotPath and DesignDecision take a single Type argument that must be a generated decision type; Role is the only free string"
  - key: ExceptionScopeIsClosed
    statement: "ExceptionScope is a closed enum (Boundary, HotPath, Pool, Interop, Compatibility, Migration) mirrored by a SKOS scheme in the ledger vocabulary"
  - key: AttributeArgumentsMustBeGenerated
    statement: "DD0007: the decision argument must be a generator-emitted type and required named arguments must be present. Generator-emitted is established by two signals together, both required: the type carries System.CodeDom.Compiler.GeneratedCode naming DecisionDriven.Analyzers as the tool, and its declaring syntax tree is in the directory this compilation's generator run placed its output in, which is where the generated DecisionDriven.ContractAttribute is declared. Roslyn roots that directory in the compiler's output directory in a real build, so the path shape alone is two directories anyone can create; the directory the run actually used is not. Neither a comment header nor a marker attribute of our own is a provenance signal, because both are text anyone can write"
  - key: NoPragmaOrSuppressMessage
    statement: "DD0008: pragma and SuppressMessage for DD/product rules are errors and .editorconfig downgrades below a rule tier are errors; DesignDecision on the symbol is the only exception path"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A07-*.md.
