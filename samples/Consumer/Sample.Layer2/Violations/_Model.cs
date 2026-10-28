#if DD_SAMPLE_VIOLATIONS
// Not a violation: the scaffolding the model-surface violations need. DD0013, DD0014, DD0015 and
// DD0019 are about types in a [DomainModel] namespace, and this declares one. The namespace is
// Sample.Layer2.Violations.Model because the files that declare it live in Violations/Model/,
// which is what DD0022 asks; the prefix is configuration, so the configuration moved with them.
//
// IncludeSubNamespaces = false (DecisionsAsTypes.DomainModelIncludesSubNamespaces): only
// Sample.Layer2.Violations.Model is model. Sample.Layer2.Violations.Model.Stores, in
// Model/Stores/, is not, and the store in it would be DD0014's and DD0019's finding a second time
// if the package took sub-namespaces in, which violations.expected's "exactly once" would catch.
//
// This file declares no namespace and no type, so DD0022 has nothing to report on it.
[assembly: global::DecisionDriven.DomainModel("Sample.Layer2.Violations.Model", typeof(global::DecisionDriven.Ledger.DddAnalyzers.PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract), IncludeSubNamespaces = false)]
#endif
