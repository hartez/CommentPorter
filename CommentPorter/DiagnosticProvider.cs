using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommentPorter
{
    internal partial class Program
    {
        private class DiagnosticProvider : FixAllContext.DiagnosticProvider
        {
            readonly IList<Diagnostic> _diagnostics;

            public DiagnosticProvider(IList<Diagnostic> diagnostics)
            {
                _diagnostics = diagnostics;
            }

            public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            {
                return Task.FromResult(_diagnostics.AsEnumerable());
            }

            public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            {
                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                return _diagnostics.Where(d => tree == d.Location.SourceTree);
            }

            public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            {
                return GetAllDiagnosticsAsync(project, cancellationToken);
            }
        }
    }
}
