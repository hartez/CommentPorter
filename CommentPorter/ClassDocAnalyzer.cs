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
        internal static readonly LocalizableString Title = "Public classes and members should point to existing class docs";
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

            // TODO ezhart need to handle enum, struct
            context.RegisterSyntaxNodeAction(CheckForMissingEnumInclude, SyntaxKind.EnumDeclaration);

            // TODO Then we need to figure out what stuff is missing and why
            // Probably mismatching docs filenames and class/struct/enum names

            context.RegisterSyntaxNodeAction(CheckForMissingMethodInclude, SyntaxKind.MethodDeclaration);
        }

        public const string DocumentationFileKey = "docfile";
        public const string XPathKey = "xpath";

        static bool HasDocComment(CSharpSyntaxNode node) 
        {
            return node.GetLeadingTrivia()
                   .Select(i => i.GetStructure())
                   .OfType<DocumentationCommentTriviaSyntax>()
                   .FirstOrDefault() != null;
        }

        static bool IsPublic(MemberDeclarationSyntax syntax)
        {
            return syntax.Modifiers.Any(SyntaxKind.PublicKeyword);
        }

        static string FindNamespace(SyntaxNodeAnalysisContext context) 
        {
            var nsds = context.Node.Ancestors().OfType<NamespaceDeclarationSyntax>().First();
            return nsds.Name.ToString();
        }

        ImmutableDictionary<string, string> BuildProps(string docPath, string xpath) 
        {
            var propsBuilder = ImmutableDictionary.CreateBuilder<string, string>();
            propsBuilder.Add(DocumentationFileKey, docPath);
            propsBuilder.Add(XPathKey, xpath);
            return propsBuilder.ToImmutable();
        }

        void CheckForMissingClassInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as ClassDeclarationSyntax;

            if (!IsPublic(node)) 
            {
                return;
            }

            if (HasDocComment(node)) 
            {
                return;
            }

            

            var namespaceName = FindNamespace(syntaxNodeAnalysisContext);

            var unitName = node.Identifier.ValueText;
            var filePath = node.GetLocation().SourceTree.FilePath;

            var docPath = DocFinder.BuildRelativeDocPath(unitName, filePath);

            if (docPath == null) 
            {
                // There's no corresponding documentation file to link to 
                return;
            }

            string xpath = $"Type[@FullName='{namespaceName}.{unitName}'/Docs";

            var props = BuildProps(docPath, xpath);

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments on class");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        void CheckForMissingEnumInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as EnumDeclarationSyntax;

            if (!IsPublic(node))
            {
                return;
            }

            if (HasDocComment(node))
            {
                return;
            }

            var namespaceName = FindNamespace(syntaxNodeAnalysisContext);

            var unitName = node.Identifier.ValueText;
            var filePath = node.GetLocation().SourceTree.FilePath;

            var docPath = DocFinder.BuildRelativeDocPath(unitName, filePath);

            if (docPath == null)
            {
                // There's no corresponding documentation file to link to 
                return;
            }

            string xpath = $"Type[@FullName='{namespaceName}.{unitName}'/Docs";

            var props = BuildProps(docPath, xpath);

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments on class");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        void CheckForMissingMethodInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as MethodDeclarationSyntax;

            if (!IsPublic(node))
            {
                return;
            }

            if (HasDocComment(node))
            {
                return;
            }

            var propsBuilder = ImmutableDictionary.CreateBuilder<string, string>();

            var methodName = node.Identifier.ValueText;

            var declarationName = GetContainingDeclarationName(node);

            var filePath = node.GetLocation().SourceTree.FilePath;

            var docPath = DocFinder.BuildRelativeDocPath(declarationName, filePath);

            if (docPath == null)
            {
                // There's no corresponding documentation file to link to 
                return;
            }

            string xpath = $"//Member[@MemberName='{methodName}']/Docs";

            propsBuilder.Add(DocumentationFileKey, docPath);
            propsBuilder.Add(XPathKey, xpath);

            var props = propsBuilder.ToImmutable();
            
            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments on method");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        string GetContainingDeclarationName(SyntaxNode node) 
        {
            var ancestors = node.Ancestors();

            var classDeclaration = ancestors.OfType<ClassDeclarationSyntax>().FirstOrDefault();

            if (classDeclaration != null)
            {
                return classDeclaration.Identifier.ValueText;
            }

            var enumDeclaration = ancestors.OfType<EnumDeclarationSyntax>().FirstOrDefault();

            if (enumDeclaration != null) 
            {
                return enumDeclaration.Identifier.ValueText;
            }

            var structDeclaration = ancestors.OfType<StructDeclarationSyntax>().FirstOrDefault();

            return structDeclaration.Identifier.ValueText;
        }
    }
}
