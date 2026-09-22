---
set: contracts
namespace: ddd-analyzers
source-draft: ADR-A08
decisions:
  - key: ContractsCarryAttribute
    statement: "DD0009: every public interface, abstract class and delegate in a layered project carries Contract citing a decision, with no member-count threshold"
  - key: ContractVocabularyAllowList
    statement: "DD0010: types on contract signatures come from the BCL, the assembly DomainModel namespaces, ArchContractTypeAssemblies, or are themselves Contract-marked"
  - key: DataVersusCollaboratorParameters
    statement: "DD0011: contract parameters are model types, delegates, spans, CancellationToken, enums, type parameters or Contract-marked types; ad-hoc interface parameters and object are errors"
  - key: NoNotSupportedFromContractMember
    statement: "DD0012: throwing NotSupportedException from an implemented contract member or override is an error"
  - key: MemberCountIsReport
    statement: "Interface member count and implementer/caller divergence are the tier-3 ISP signal, never a gate"
---

Interim set file (decisions-as-types, InterimFrontMatterUntilExport). No `accepted-by`: every decision here is unaccepted until a holder with accept-decision accepts it in the ledger. Narrative: docs/drafts/ADR-A08-*.md.
