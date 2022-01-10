using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CommentPorter
{
    internal class ClassDocFixer : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; }
           = ImmutableArray.Create(ClassDocAnalyzer.DiagnosticId);

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Add missing class doc include",
                        cancellationToken => GetTransformedDocumentAsync(context.Document, diagnostic, cancellationToken),
                        nameof(ClassDocFixer)),
                    diagnostic);
            }

            return Task.CompletedTask;
        }

        static async Task<Document> GetTransformedDocumentAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            return document.WithText(text.WithChanges(GetTextChange(root, diagnostic)));
        }

        static TextChange GetTextChange(SyntaxNode root, Diagnostic diagnostic)
        {
            diagnostic.Properties.TryGetValue(ClassDocAnalyzer.DocumentationFileKey, out string documentationFile);
            diagnostic.Properties.TryGetValue(ClassDocAnalyzer.ClassFullNameKey, out string classFullName);

            var token = root.FindToken(diagnostic.Location.SourceSpan.Start, findInsideTrivia: true);
            var tagText = $"/// <include file=\"{documentationFile}\" path=\"//Type[@FullName='{classFullName}']/Docs\" />\n\t";
            
            return new TextChange(new TextSpan(token.SpanStart, 0), tagText);
        }
    }
}
