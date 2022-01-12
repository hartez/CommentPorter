using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommentPorter
{
    internal partial class Program
    {
        static async Task Main(string[] args)
        {
            string mauiFolder = "C:\\maui\\maui\\src";

            // Attempt to set the version of MSBuild.
            var visualStudioInstances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            var instance = visualStudioInstances.Length == 1
                // If there is only one instance of MSBuild on this machine, set that as the one to use.
                ? visualStudioInstances[0]
                // Handle selecting the version of MSBuild you want to use.
                : SelectVisualStudioInstance(visualStudioInstances);

            Console.WriteLine($"Using MSBuild at '{instance.MSBuildPath}' to load projects.");
            
            MSBuildLocator.RegisterInstance(instance);

            var projectPaths = new List<string>() 
            {
                Path.Combine(mauiFolder, "Core\\src\\Core-net6.csproj"),
                Path.Combine(mauiFolder, "Controls\\src\\Core\\Controls.Core-net6.csproj")
            };

            var rootNamespaces = new List<string>()
            {
                "Microsoft.Maui",
                "Microsoft.Maui.Controls"
            };

            var docsPaths = new List<string>() 
            {
                Path.Combine(mauiFolder, "Core\\docs"),
                Path.Combine(mauiFolder, "Controls\\docs")
            };

            DocFinder.DocsSource = "C:\\maui\\Xamarin.Forms-api-docs\\docs";

            for (int n = 0; n < projectPaths.Count; n++) 
            {
                await UpdateDocsForProject(projectPaths[n], docsPaths[n], rootNamespaces[n]);
            }            
        }

        static async Task UpdateDocsForProject(string projectPath, string docsPath, string namespaceRoot) 
        {
            CancellationToken cancellationToken = CancellationToken.None;

            DocFinder.DocsPath = docsPath;
            DocFinder.NamespaceMap = new NamespaceMap(namespaceRoot);

            // Clean out the docs folder if it exists
            if (Directory.Exists(docsPath))
            {
                Directory.Delete(docsPath, true);    
            }

            // And create it anew
            Directory.CreateDirectory(docsPath);

            using (var workspace = MSBuildWorkspace.Create())
            {
                // Print message for WorkspaceFailed event to help diagnosing project load failures.
                workspace.WorkspaceFailed += (o, e) => Console.WriteLine(e.Diagnostic.Message);

                Console.WriteLine($"Loading project '{projectPath}'");

                // Attach progress reporter so we print projects as they are loaded.
                var project = await workspace.OpenProjectAsync(projectPath, new ConsoleProgressReporter());
                Console.WriteLine($"Finished loading project '{projectPath}'");

                var analyzers = ImmutableArray.Create((DiagnosticAnalyzer)new ClassDocAnalyzer());
                var fixes = ImmutableArray.Create(new ClassDocFixer());

                var compilation = await project.GetCompilationAsync();

                var results = await compilation
                    .WithAnalyzers(analyzers)
                    .GetAnalyzerDiagnosticsAsync();

                var fixedDiagnostics = new List<string>();

                foreach (var diagnostic in results)
                {
                    var diagnosticId = diagnostic.Id;

                    if (fixedDiagnostics.Contains(diagnosticId))
                    {
                        // Already got this one, probably during a bulk operation
                        continue;
                    }

                    Console.WriteLine($"Working on {diagnostic.Descriptor.Title}...");

                    var fixProvider = fixes.Where(f => f.FixableDiagnosticIds.Contains(diagnosticId)).FirstOrDefault();

                    if (fixProvider != null)
                    {
                        var changes = await GetChangesForDiagnostic(project, diagnostic, fixProvider,
                            new DiagnosticProvider(results), cancellationToken);

                        Console.WriteLine($"Determined changes for {diagnostic.Descriptor.Title}, applying...");

                        // TODO ezhart something is borking the line endings when this edits
                        changes.Apply(workspace, cancellationToken);

                        Console.WriteLine($"Changes for {diagnostic.Descriptor.Title} applied.");
                    }

                    fixedDiagnostics.Add(diagnosticId);
                }

                Console.WriteLine($"Done!");
            }
        }

        static async Task<ApplyChangesOperation> GetChangesForDiagnostic(Project project, Diagnostic diagnostic, CodeFixProvider codeFixProvider,
            DiagnosticProvider diagnosticProvider,
            CancellationToken cancellationToken) 
        {
            var document = project.GetDocument(diagnostic.Location.SourceTree);

            CodeAction action = null;
            var context = new CodeFixContext(document, diagnostic,
                (a, _) =>
                {
                    if (action == null)
                    {
                        action = a;
                    }
                },
                cancellationToken);

            var fixAllProvider = codeFixProvider.GetFixAllProvider();

            await codeFixProvider.RegisterCodeFixesAsync(context);

            var fixAllContext = new FixAllContext(
              document: document,
              codeFixProvider: codeFixProvider,
              scope: FixAllScope.Project,
              codeActionEquivalenceKey: action.EquivalenceKey, // FixAllState supports null equivalence key. This should still be supported.
              diagnosticIds: new[] { diagnostic.Id },
              fixAllDiagnosticProvider: diagnosticProvider,
              cancellationToken: cancellationToken);

            var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
            if (fixAllAction is null)
            {
                Console.WriteLine($"Something didn't work with fixAllAction");
                return null;
            }

            var operations = await fixAllAction.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
            if (applyChangesOperation is null)
            {
                Console.WriteLine($"Something didn't work with operations");
                return null;
            }

           return applyChangesOperation;
        }

        private static VisualStudioInstance SelectVisualStudioInstance(VisualStudioInstance[] visualStudioInstances)
        {
            Console.WriteLine("Multiple installs of MSBuild detected please select one:");
            for (int i = 0; i < visualStudioInstances.Length; i++)
            {
                Console.WriteLine($"Instance {i + 1}");
                Console.WriteLine($"    Name: {visualStudioInstances[i].Name}");
                Console.WriteLine($"    Version: {visualStudioInstances[i].Version}");
                Console.WriteLine($"    MSBuild Path: {visualStudioInstances[i].MSBuildPath}");
            }

            while (true)
            {
                var userResponse = Console.ReadLine();
                if (int.TryParse(userResponse, out int instanceNumber) &&
                    instanceNumber > 0 &&
                    instanceNumber <= visualStudioInstances.Length)
                {
                    return visualStudioInstances[instanceNumber - 1];
                }
                Console.WriteLine("Input not accepted, try again.");
            }
        }

        private class ConsoleProgressReporter : IProgress<ProjectLoadProgress>
        {
            public void Report(ProjectLoadProgress loadProgress)
            {
                var projectDisplay = Path.GetFileName(loadProgress.FilePath);
                if (loadProgress.TargetFramework != null)
                {
                    projectDisplay += $" ({loadProgress.TargetFramework})";
                }

                Console.WriteLine($"{loadProgress.Operation,-15} {loadProgress.ElapsedTime,-15:m\\:ss\\.fffffff} {projectDisplay}");
            }
        }
    }
}
