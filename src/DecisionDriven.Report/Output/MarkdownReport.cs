using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DecisionDriven.Analyzers.Ledger;
using DecisionDriven.Report.Citations;
using DecisionDriven.Report.Metrics;

namespace DecisionDriven.Report.Output;

/// <summary>
/// The report a person reads: in CI, the job summary.
/// </summary>
/// <remarks>
/// <c>WholeGraphReport.NothingGatesUntilBaselineDecision</c>: every number here is information. The
/// wording says what each section is a signal of and stops there, because deciding what to do about
/// an uncited decision or an unstable low layer is a decision, and the report is not the place it
/// gets made.
/// </remarks>
internal static class MarkdownReport
{
    internal static string Write(
        string version,
        string? head,
        IReadOnlyList<PackageRow> packages,
        IReadOnlyList<ContractRow> contracts,
        IReadOnlyList<CohesionRow> cohesion,
        Projection projection)
    {
        StringBuilder md = new StringBuilder();

        md.Append("# Decision-driven report\n\n");
        md.Append("DecisionDriven.Report ").Append(version);
        if (head is not null)
        {
            md.Append(" at `").Append(head.Length > 12 ? head.Substring(0, 12) : head).Append('`');
        }

        md.Append(". Nothing here gates a build: a metric becomes a gate only by a decision that names its threshold and baseline.\n\n");

        Packages(md, packages);
        Contracts(md, contracts);
        Cohesion(md, cohesion);
        Citations(md, projection);

        return md.ToString();
    }

    private static void Packages(StringBuilder md, IReadOnlyList<PackageRow> packages)
    {
        md.Append("## Layers\n\n");

        if (packages.Count == 0)
        {
            md.Append("No assembly declares a layer.\n\n");
            return;
        }

        md.Append("Instability should fall as the layer does. An assembly marked ⚠ is less stable than something above it.\n\n");
        md.Append("| Assembly | Layer | Ca | Ce | I | A | D | |\n");
        md.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");

        foreach (PackageRow row in packages)
        {
            md.Append("| `").Append(row.Assembly).Append("` | ")
                .Append(row.Layer.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(row.Afferent.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(row.Efferent.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(Number(row.Instability)).Append(" | ")
                .Append(Number(row.Abstractness)).Append(" | ")
                .Append(Number(row.Distance)).Append(" | ")
                .Append(row.ContradictsLayer ? "⚠" : string.Empty).Append(" |\n");
        }

        md.Append('\n');
    }

    private static void Contracts(StringBuilder md, IReadOnlyList<ContractRow> contracts)
    {
        md.Append("## Contracts\n\n");

        if (contracts.Count == 0)
        {
            md.Append("No `[Contract]` interface.\n\n");
            return;
        }

        md.Append("Members offered against members each caller takes. A contract every caller uses a small part of is a candidate for splitting.\n\n");
        md.Append("| Contract | Members | Implementers | Callers |\n");
        md.Append("| --- | ---: | ---: | --- |\n");

        foreach (ContractRow row in contracts)
        {
            md.Append("| `").Append(row.Contract).Append("` | ")
                .Append(row.Members.Count.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(row.Implementers.ToString(CultureInfo.InvariantCulture)).Append(" | ");

            md.Append(row.Callers.Count == 0
                ? "none"
                : string.Join("<br>", row.Callers.Select(caller =>
                    "`" + caller.Caller + "` uses " + caller.Members.Count.ToString(CultureInfo.InvariantCulture)
                    + " of " + row.Members.Count.ToString(CultureInfo.InvariantCulture)
                    + ": " + string.Join(", ", caller.Members))));

            md.Append(" |\n");
        }

        md.Append('\n');
    }

    private static void Cohesion(StringBuilder md, IReadOnlyList<CohesionRow> cohesion)
    {
        md.Append("## Model cohesion (LCOM4)\n\n");

        if (cohesion.Count == 0)
        {
            md.Append("No type in a `[DomainModel]` namespace.\n\n");
            return;
        }

        md.Append("The number of groups a type's methods fall into, where methods sharing a field or calling each other are one group. One is one responsibility.\n\n");
        md.Append("| Type | LCOM4 | Methods |\n");
        md.Append("| --- | ---: | ---: |\n");

        foreach (CohesionRow row in cohesion)
        {
            md.Append("| `").Append(row.Type).Append("` | ")
                .Append(row.Components.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(row.Methods.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        md.Append('\n');
    }

    private static void Citations(StringBuilder md, Projection projection)
    {
        md.Append("## Citations\n\n");

        int decisions = projection.Citations.Select(c => c.Citation.DecisionId).Distinct(StringComparer.Ordinal).Count();
        md.Append(projection.Citations.Count.ToString(CultureInfo.InvariantCulture)).Append(" citations of ")
            .Append(decisions.ToString(CultureInfo.InvariantCulture)).Append(" decisions.\n\n");

        md.Append("### Decisions with no citation\n\n");
        md.Append("Implicit somewhere, or dead. The report does not say which.\n\n");
        List(md, projection.Uncited.Select(Describe));

        md.Append("### Citations of a version that is no longer the tip\n\n");
        md.Append("The decision changed after the code that cites it was written.\n\n");
        List(md, projection.Citations.Where(c => c.Stale).Select(Describe));

        if (projection.Since is { } since)
        {
            md.Append("### Newly cited since `").Append(since).Append("`\n\n");
            md.Append("What the authors of these changes, human or agent, said they were doing.\n\n");
            List(md, projection.Citations
                .Where(c => c.NewSince)
                .Select(c => c.Citation.DecisionId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => "`" + id + "`"));
        }

        List<Attributed> undated = projection.Citations.Where(c => c.Commit is null).ToList();
        if (undated.Count > 0)
        {
            md.Append("### Citations with no introducing commit\n\n");
            md.Append("Not committed yet, or written in a file the assembly's PDB does not name.\n\n");
            List(md, undated.Select(Describe));
        }
    }

    private static string Describe(Decision decision) =>
        "`" + decision.Id + "`" + (decision.Tip?.Statement is { Length: > 0 } statement ? " — " + statement : string.Empty);

    private static string Describe(Attributed attributed) =>
        "`" + attributed.Citation.Symbol + "` [" + attributed.Citation.Attribute + "] cites `" + attributed.Citation.DecisionId + "`"
        + (attributed.SourceFile is { } file ? " in `" + file + "`" : string.Empty);

    private static void List(StringBuilder md, IEnumerable<string> items)
    {
        bool any = false;
        foreach (string item in items)
        {
            md.Append("- ").Append(item).Append('\n');
            any = true;
        }

        md.Append(any ? "\n" : "None.\n\n");
    }

    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
