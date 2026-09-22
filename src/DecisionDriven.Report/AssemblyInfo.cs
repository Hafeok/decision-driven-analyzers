using System.Runtime.CompilerServices;

// Nothing in this tool is public: it is an executable, and its surface is its command
// line. The tests reach the pieces behind that command line from here.
[assembly: InternalsVisibleTo("DecisionDriven.Report.Tests")]
