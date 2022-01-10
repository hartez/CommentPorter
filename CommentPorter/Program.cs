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
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Attempt to set the version of MSBuild.
            var visualStudioInstances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            var instance = visualStudioInstances.Length == 1
                // If there is only one instance of MSBuild on this machine, set that as the one to use.
                ? visualStudioInstances[0]
                // Handle selecting the version of MSBuild you want to use.
                : SelectVisualStudioInstance(visualStudioInstances);

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

                foreach (var diagnostic in results)
                {
                    var diagnosticId = diagnostic.Id;

                    Console.WriteLine($"Severity: {diagnostic.Severity}\tMessage: {diagnostic.GetMessage()}");

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

                    var codeFixProvider = fixes[0];
                    var fixAllProvider = codeFixProvider.GetFixAllProvider();

                    await codeFixProvider.RegisterCodeFixesAsync(context);

                    var fixAllContext = new FixAllContext(
                  document: document,
                  codeFixProvider: codeFixProvider,
                  scope: FixAllScope.Project,
                  codeActionEquivalenceKey: action.EquivalenceKey, // FixAllState supports null equivalence key. This should still be supported.
                  diagnosticIds: new[] { diagnosticId },
                  fixAllDiagnosticProvider: new DiagnosticProvider(results),
                  cancellationToken: cancellationToken);

                    var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
                    if (fixAllAction is null)
                    {
                        Console.WriteLine($"Something didn't work with fixAllAction");
                        continue;
                    }

                    var operations = await fixAllAction.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
                    var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
                    if (applyChangesOperation is null)
                    {
                        Console.WriteLine($"Something didn't work with operations");
                        continue;
                    }

                    applyChangesOperation.Apply(workspace, cancellationToken);

                    break; // Wrong, but we'll add the bits to skip the diagnostic types we've already done later
                }

                Console.WriteLine($"Analysis complete");
            }
        }

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
