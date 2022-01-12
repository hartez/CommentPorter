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
        public const string DocumentationFileKey = "docfile";
        public const string XPathKey = "xpath";

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

            context.RegisterSyntaxNodeAction(CheckForMissingInclude, SyntaxKind.ClassDeclaration, 
                SyntaxKind.EnumDeclaration, SyntaxKind.StructDeclaration);

            context.RegisterSyntaxNodeAction(CheckForMissingMemberInclude, SyntaxKind.MethodDeclaration, 
                SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration);
        }

        void CheckForMissingInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as MemberDeclarationSyntax;

            if (!IsPublic(node) || HasDocComment(node)) 
            {
                return;
            }

            var namespaceName = FindNamespace(syntaxNodeAnalysisContext);

            var unitName = GetUnitName(syntaxNodeAnalysisContext);
            var filePath = node.GetLocation().SourceTree.FilePath;

            var docPath = DocFinder.BuildRelativeDocPath(unitName, namespaceName, filePath);

            if (docPath == null) 
            {
                // There's no corresponding documentation file to link 
                System.Diagnostics.Debug.WriteLine($"Was looking for docs for {unitName}, didn't find anything.");
                return;
            }

            string xpath = $"Type[@FullName='{namespaceName}.{unitName}']/Docs";

            var props = BuildProps(docPath, xpath);

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        void CheckForMissingMemberInclude(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var node = syntaxNodeAnalysisContext.Node as MemberDeclarationSyntax;

            if (!IsPublic(node) || HasDocComment(node))
            {
                return;
            }

            if (node.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.OverridesKeyword)))
            {
                // TODO ezhart Isn't there <inheritDoc/> for override methods?
                // Can probably report a different diagnostic ID here and have a different fix that just adds <inheritDoc/>
            }

            var memberName = GetUnitName(syntaxNodeAnalysisContext);

            var declarationName = GetContainingDeclarationName(node);

            var filePath = node.GetLocation().SourceTree.FilePath;

            var namespaceName = FindNamespace(syntaxNodeAnalysisContext);

            var docPath = DocFinder.BuildRelativeDocPath(declarationName, namespaceName, filePath);

            if (docPath == null)
            {
                // There's no corresponding documentation file to link to 
                return;
            }

            string xpath = $"//Member[@MemberName='{memberName}']/Docs";

            var props = BuildProps(docPath, xpath);

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments");
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

            if (structDeclaration != null) 
            {
                return structDeclaration.Identifier.ValueText;
            }

            var interfaceDeclaration = ancestors.OfType<InterfaceDeclarationSyntax>().FirstOrDefault();

            if (interfaceDeclaration != null)
            {
                return interfaceDeclaration.Identifier.ValueText;
            }

            throw new System.Exception($"Not prepared for {node}");
        }

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

        static string GetUnitName(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            if (syntaxNodeAnalysisContext.Node is ClassDeclarationSyntax node)
            {
                return node.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is EnumDeclarationSyntax enumNode)
            {
                return enumNode.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is StructDeclarationSyntax structNode)
            {
                return structNode.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is MethodDeclarationSyntax methodNode)
            {
                return methodNode.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is PropertyDeclarationSyntax propertyNode)
            {
                return propertyNode.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is FieldDeclarationSyntax fieldNode)
            {
                // We're assuming nobody is doing public fields like
                // public int x, y;
                return fieldNode.Declaration.Variables[0].Identifier.ValueText;
            }

            throw new System.Exception($"Not prepared for {syntaxNodeAnalysisContext}");
        }
    }
}
