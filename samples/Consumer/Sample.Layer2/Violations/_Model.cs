#if DD_SAMPLE_VIOLATIONS
// Not a violation: the scaffolding the model-surface violations need. DD0013, DD0014, DD0015 and
// DD0019 are about types in a [DomainModel] namespace, and this declares one.
[assembly: global::DecisionDriven.DomainModel("Sample.Layer2.Model", typeof(global::DecisionDriven.Ledger.DddAnalyzers.PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract))]
#endif
