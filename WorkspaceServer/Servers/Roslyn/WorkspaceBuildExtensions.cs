using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Clockwise;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using WorkspaceServer.Models.Execution;
using WorkspaceServer.Servers.Roslyn.Instrumentation;
using WorkspaceServer.Transformations;
using WorkspaceServer.Workspaces;
using Workspace = WorkspaceServer.Models.Execution.Workspace;

namespace WorkspaceServer.Servers.Roslyn
{
    public static class WorkspaceBuildExtensions
    {
        private static readonly Lazy<SyntaxTree> _instrumentationEmitterSyntaxTree =new Lazy<SyntaxTree>(GetInstrumentationEmitterSyntaxTree);

        public static async Task<Compilation> Compile(this WorkspaceBuild build, Workspace workspace, Budget budget, BufferId activeBufferId)
        {
            var sourceFiles = workspace.GetSourceFiles()
                                       .Concat(await build.GetSourceFiles())
                                       .ToArray();

            var (compilation, documents) = await build.GetCompilation(sourceFiles, budget);

            var viewports = new BufferInliningTransformer().ExtractViewPorts(workspace);

            if (workspace.IncludeInstrumentation)
            {
                var activeDocument = GetActiveDocument(documents, activeBufferId);
                compilation = await AugmentCompilationAsync(viewports, compilation, activeDocument, activeBufferId);
            }

            return compilation;
        }

        private static async Task<Compilation> AugmentCompilationAsync(
            IEnumerable<Viewport> viewports, 
            Compilation compilation, 
            Document document,
            BufferId activeBufferId)
        {
            var regions = InstrumentationLineMapper.FilterActiveViewport(viewports, activeBufferId)
                .Where(v => v.Destination?.Name != null)
                .GroupBy(v => v.Destination.Name,
                         v => v.Region,
                        (name, region) => new InstrumentationMap(name, region)
            );

            var solution = document.Project.Solution;
            var newCompilation = compilation;
            foreach (var tree in newCompilation.SyntaxTrees)
            {
                var replacementRegions = regions?.Where(r => tree.FilePath.EndsWith(r.FileToInstrument)).FirstOrDefault()?.InstrumentationRegions;

                var subdocument = solution.GetDocument(tree);
                var visitor = new InstrumentationSyntaxVisitor(subdocument, await subdocument.GetSemanticModelAsync(), replacementRegions);
                var linesWithInstrumentation = visitor.Augmentations.Data.Keys;

                var activeViewport = viewports.DefaultIfEmpty(null).First();

                var (augmentationMap, variableLocationMap) =
                    await InstrumentationLineMapper.MapLineLocationsRelativeToViewportAsync(
                        visitor.Augmentations,
                        visitor.VariableLocations,
                        document,
                        activeViewport);

                var rewrite = new InstrumentationSyntaxRewriter(
                    linesWithInstrumentation,
                    variableLocationMap,
                    augmentationMap);
                var newRoot = rewrite.Visit(tree.GetRoot());
                var newTree = tree.WithRootAndOptions(newRoot, tree.Options);

                newCompilation = newCompilation.ReplaceSyntaxTree(tree, newTree);
            }

            newCompilation = newCompilation.AddSyntaxTrees(_instrumentationEmitterSyntaxTree.Value);
           
            var augmentedDiagnostics = newCompilation.GetDiagnostics();
            if (augmentedDiagnostics.Any(e => e.Severity == DiagnosticSeverity.Error))
            {
                throw new InvalidOperationException(
                    "Augmented source failed to compile: " + 
                    string.Join(Environment.NewLine, augmentedDiagnostics) + 
                    Environment.NewLine + 
                    Environment.NewLine + 
                    newCompilation.SyntaxTrees.Join(Environment.NewLine + Environment.NewLine));
            }

            return newCompilation;
        }

        private static SyntaxTree GetInstrumentationEmitterSyntaxTree()
        {
            var resourceName = "WorkspaceServer.Servers.Roslyn.Instrumentation.InstrumentationEmitter.cs"; // $"{typeof(InstrumentationEmitter).FullName}.cs";

            var assembly = typeof(WorkspaceBuildExtensions).Assembly;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException($"Resource \"{resourceName}\" not found"), Encoding.UTF8))
            {
                var source = reader.ReadToEnd();
               
                var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source));

                return syntaxTree;
            }
        }

        public static async Task<(Compilation compilation, IReadOnlyCollection<Document> documents)> GetCompilation(
            this WorkspaceBuild build,
            IReadOnlyCollection<SourceFile> sources,
            Budget budget)
        {
            var projectId = ProjectId.CreateNewId();

            var workspace = await build.GetRoslynWorkspace(projectId);

            var currentSolution = workspace.CurrentSolution;

            foreach (var source in sources)
            {
                if (currentSolution.Projects
                                   .SelectMany(p => p.Documents)
                                   .FirstOrDefault(d => d.Name == source.Name) is Document document)
                {
                    // there's a pre-existing document, so overwrite it's contents
                    document = document.WithText(source.Text);
                    currentSolution = document.Project.Solution;
                }
                else
                {
                    var docId = DocumentId.CreateNewId(projectId, $"{build.Name}.Document");

                    currentSolution = currentSolution.AddDocument(docId, source.Name, source.Text);
                }

            }

            var project = currentSolution.GetProject(projectId);

            var compilation = await project.GetCompilationAsync().CancelIfExceeds(budget);

            return (compilation, project.Documents.ToArray());
        }

        public static async Task<AdhocWorkspace> GetRoslynWorkspace(this WorkspaceBuild build, ProjectId projectId = null)
        {
            await build.EnsureBuilt();

            projectId = projectId ?? ProjectId.CreateNewId(build.Name);

            var buildLog = build.Directory.GetFiles("msbuild.log").SingleOrDefault();

            var commandLineArgs = buildLog?.FindCompilerCommandLine()?.ToArray();

            var csharpCommandLineArguments = CSharpCommandLineParser.Default.Parse(
                commandLineArgs,
                build.Directory.FullName,
                RuntimeEnvironment.GetRuntimeDirectory());

            var projectInfo = CommandLineProject.CreateProjectInfo(
                projectId,
                build.Name,
                csharpCommandLineArguments.CompilationOptions.Language,
                csharpCommandLineArguments,
                build.Directory.FullName);

            var workspace = new AdhocWorkspace(MefHostServices.DefaultHost);

            workspace.AddProject(projectInfo);

            return workspace;
        }

        public static async Task<IEnumerable<SourceFile>> GetSourceFiles(this WorkspaceBuild build)
        {
            await build.EnsureBuilt();

            // FIX: (GetSourceFiles) include source files from compiler args

            return Enumerable.Empty<SourceFile>();
        }

        private static Document GetActiveDocument(IEnumerable<Document> documents, BufferId activeBufferId)
        {
            return documents.First(d => d.Name.Equals(activeBufferId.FileName));
        }
    }
}
