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
            // Attempt to set the version of MSBuild.
            var visualStudioInstances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            var instance = visualStudioInstances.Length == 1
                // If there is only one instance of MSBuild on this machine, set that as the one to use.
                ? visualStudioInstances[0]
                // Handle selecting the version of MSBuild you want to use.
                //: SelectVisualStudioInstance(visualStudioInstances);
            
                // Just doing this for now so I don't have to keep selecting during testing
                : visualStudioInstances[0];


            Console.WriteLine($"Using MSBuild at '{instance.MSBuildPath}' to load projects.");
            
            MSBuildLocator.RegisterInstance(instance);

            CancellationToken cancellationToken = CancellationToken.None;

            using (var workspace = MSBuildWorkspace.Create())
            {
                // Print message for WorkspaceFailed event to help diagnosing project load failures.
                workspace.WorkspaceFailed += (o, e) => Console.WriteLine(e.Diagnostic.Message);

                var projectPath = args[0];

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
