#if DD_SAMPLE_VIOLATIONS
// Not a violation: the scaffolding the model-surface violations need. DD0013, DD0014, DD0015 and
// DD0019 are about types in a [DomainModel] namespace, and this declares one.
//
// IncludeSubNamespaces = false (DecisionsAsTypes.DomainModelIncludesSubNamespaces): only
// Sample.Layer2.Model is model. Sample.Layer2.Model.Stores below is not, and the store in it would
// be DD0014's and DD0019's finding a second time if the package took sub-namespaces in, which
// violations.expected's "exactly once" would catch.
[assembly: global::DecisionDriven.DomainModel("Sample.Layer2.Model", typeof(global::DecisionDriven.Ledger.DddAnalyzers.PrimitiveFreeSurfaces.NoNakedPrimitivesOnModelAndContract), IncludeSubNamespaces = false)]

namespace Sample.Layer2.Model.Stores
{
    /// <summary>A mutable store beside the model, not in it.</summary>
    public sealed class Store
    {
        /// <summary>Gets or sets how many items it holds.</summary>
        public int Count { get; set; }
    }
}
#endif
