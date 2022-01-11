using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace CommentPorter
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class ClassDocAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "PublicClassDoc";
        internal static readonly LocalizableString Title = "Public classes should point to existing class docs";
        internal static readonly LocalizableString MessageFormat = "'{0}'";
        internal const string Category = "MAUIDocPort";

        internal static DiagnosticDescriptor Rule =
#pragma warning disable RS2008 // Enable analyzer release tracking
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, true);
#pragma warning restore RS2008 // Enable analyzer release tracking

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(CheckForMissingClassInclude, SyntaxKind.ClassDeclaration);
            context.RegisterSyntaxNodeAction(CheckForMissingMethodInclude, SyntaxKind.MethodDeclaration);
        }

        public const string DocumentationFileKey = "docfile";
        public const string ClassFullNameKey = "classfullname";

        void CheckForMissingClassInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as ClassDeclarationSyntax;

            // We only care about public classes
            if (!node.Modifiers.Any(SyntaxKind.PublicKeyword))
                return;

            var xmlTrivia = node.GetLeadingTrivia()
                .Select(i => i.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (xmlTrivia != null)
            {
                // This already has some sort of /// comment
                return;
            }

            var propsBuilder = ImmutableDictionary.CreateBuilder<string, string>();

            var nsds = syntaxNodeAnalysisContext.Node.Ancestors().OfType<NamespaceDeclarationSyntax>().First();
            var namespaceName = nsds.Name.ToString();

            var className = node.Identifier.ValueText;
            var filePath = node.GetLocation().SourceTree.FilePath;

            var docPath = DocFinder.BuildRelativeDocPath(className, filePath);

            if (docPath == null) 
            {
                // There's no corresponding documentation file to link to 
                return;
            }

            propsBuilder.Add(DocumentationFileKey, docPath);
            propsBuilder.Add(ClassFullNameKey, $"{namespaceName}.{className}");

            var props = propsBuilder.ToImmutable();

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML Comments");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);

            return;
        }
    }
}
