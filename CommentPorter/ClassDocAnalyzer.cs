using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;

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
                SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration, SyntaxKind.EnumMemberDeclaration, 
                SyntaxKind.ConstructorDeclaration);
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
                System.Diagnostics.Debug.WriteLine($"Was looking for docs for {memberName}, didn't find anything.");
                return;
            }

            var absoluteDocPath = DocFinder.BuildDocPath(DocFinder.DocsPath, namespaceName, declarationName);
            var memberIndex = GetOverloadIndex(node, absoluteDocPath, memberName);

            string xpath = $"//Member[@MemberName='{memberName}']{memberIndex}/Docs";

            var props = BuildProps(docPath, xpath);

            var diagnostic = Diagnostic.Create(Rule, node.GetLocation(), properties: props, "Missing XML comments");
            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        static string GetOverloadIndex(SyntaxNode node, string docPath, string memberName) 
        {
            string parameters = "";

            if (node is MethodDeclarationSyntax method) 
            {
                parameters = method.ParameterList.ToString().Trim();
            }

            if (node is ConstructorDeclarationSyntax constructor)
            {
                parameters = constructor.ParameterList.ToString().Trim();
            }

            if (string.IsNullOrEmpty(parameters)) 
            {
                // If the node type is not a method, return empty string
                return "";
            }

            // Load the doc so we can find the index of the correct overload
            XmlDocument doc  = new XmlDocument();
            doc.Load(docPath);

            // Find the candidates
            var memberNodes = doc.SelectNodes($"//Member[@MemberName='{memberName}']");

            // If there's only one result (or none), return the empty string
            if (memberNodes.Count < 2) 
            {
                return "";
            }

            // If there's more than one result, can we figure out the signature? 

            // The old stuff won't have nullability signifiers, so strip those out
            parameters = parameters.Replace("?", "");

            int index = 0;

            // Now search the nodes for a matching signature
            foreach (XmlNode memberNode in memberNodes) 
            {
                var memberSig = memberNode.SelectSingleNode($"MemberSignature[@Language='C#']").Attributes["Value"].Value;

                // Get the signature from the XML doc into the same format as the one from Roslyn
                var memberParameters = FormatSignature(memberSig);

                if (memberParameters == parameters) 
                {
                    return $"[{index}]";
                }

                index += 1;
            }

            return "";
        }

        static string FormatSignature(string memberSig) 
        {
            var paramsStartIndex = memberSig.IndexOf('(');
            var paramsEndIndex = memberSig.IndexOf(')');
            var memberParameters = memberSig.Substring(paramsStartIndex, paramsEndIndex + 1 - paramsStartIndex);

            if (memberParameters.Contains("<"))
            {
                // Not gonna mess with generics just yet
                return "";
            }

            // Strip out the namespaces, since the Roslyn signature info doesn't have them
            var tokens = memberParameters.Split(new[] { ',', '(', ')' }, System.StringSplitOptions.RemoveEmptyEntries);
            memberParameters = "";

            for (int n = 0; n < tokens.Length; n++)
            {
                var token = tokens[n].Trim();

                var dotIndex = token.LastIndexOf('.');
                if (dotIndex > 0)
                {
                    token = token.Substring(dotIndex + 1, token.Length - dotIndex - 1);
                }

                memberParameters += token;
                if (n < tokens.Length - 1)
                {
                    memberParameters += ", ";
                }
            }

            return $"({memberParameters})";
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
            if (IsPublicEnumMember(syntax)) 
            {
                return true;
            }

            return syntax.Modifiers.Any(SyntaxKind.PublicKeyword);
        }

        static bool IsPublicEnumMember(MemberDeclarationSyntax syntax) 
        {
            // We don't explicitly mark members of public enums as 'public', so we need to check
            // that an enum member declaration is part of a public enum; looking for the keyword
            // like we do with other declarations doesn't work.

            var node = syntax as EnumMemberDeclarationSyntax;
            if (node == null) 
            {
                return false;
            }

            if (node.Parent is EnumDeclarationSyntax enumDeclaration) 
            {
                return IsPublic(enumDeclaration);
            }

            return false;
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

            if (syntaxNodeAnalysisContext.Node is EnumMemberDeclarationSyntax enumMemberNode)
            {
                return enumMemberNode.Identifier.ValueText;
            }

            if (syntaxNodeAnalysisContext.Node is ConstructorDeclarationSyntax constructorNode)
            {
                return ".ctor";
            }

            throw new System.Exception($"Not prepared for {syntaxNodeAnalysisContext}");
        }
    }
}
