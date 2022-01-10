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
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(CheckForMissingInclude, SyntaxKind.ClassDeclaration);
        }

        public const string DocumentationFileKey = "docfile";
        public const string ClassFullNameKey = "classfullname";

        void CheckForMissingInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
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

            // TODO ezhart This value needs to include the full relative path e.g. ../../docs/Easing.xml
            propsBuilder.Add(DocumentationFileKey, node.Identifier.ValueText + ".xml");
            propsBuilder.Add(ClassFullNameKey, $"{namespaceName}.{node.Identifier.ValueText}");

            var props = propsBuilder.ToImmutable();

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML Comments");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);

            return;
        }
    }
}
