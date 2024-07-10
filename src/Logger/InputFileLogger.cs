using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using Microsoft.Build.Framework;

namespace Logger;

public sealed class InputFileLogger : ILogger
{
    public LoggerVerbosity Verbosity { get; set; }
    public string? Parameters { get; set; }

    private readonly struct ReferenceInfo(string include, string path) : IEquatable<ReferenceInfo>
    {
        public string Include { get; } = include;
        public string Path { get; } = path;

        public bool Equals(ReferenceInfo other)
        {
            return string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

        public override int GetHashCode()
                {
                return string.GetHashCode(Path, StringComparison.Ordinal);
                }

                
    }

    private sealed class ProjectInfo
    {
        public HashSet<string> Files { get; }
        public HashSet<ReferenceInfo> Projects { get; }
        public string ProjectFile { get; }
        public string ProjectRelativeFile { get; }
        public string Name { get; }

        public ProjectInfo(string projectFile)
        {
            Files = new HashSet<string>(StringComparer.Ordinal);
            Projects = new HashSet<ReferenceInfo>();
            ProjectFile = projectFile;
            ProjectRelativeFile = GetRelativePath(projectFile);
            Name = Path.GetFileNameWithoutExtension(projectFile);
        }

        public void Insert(string? key, string value, ITaskItem item)
        {
            if (key == null || key.StartsWith("_")) {
                return;
            }

            if (key == "ProjectReference") {
                var path = item.GetMetadata("Identity").Replace('/', '\\');
                Console.WriteLine(string.Join(",", item.MetadataNames.OfType<string>()));
                lock(Projects)
                {
                    Projects.Add(new ReferenceInfo(path, value));
                }
            } else {
                
            lock (Files)
            {
                if (Files.Add(value))
                {
                    Console.WriteLine($"Added {key}={value}");
                }
            }
            }
            
        }
    }

    private readonly HashSet<string> _trackedFiles = new HashSet<string>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProjectInfo> _projectFiles = new ConcurrentDictionary<string, ProjectInfo>();

    public void Initialize(IEventSource eventSource)
    {
        var trackedFiles = Environment.GetEnvironmentVariable("TRACKED_FILES");
        if (trackedFiles == null)
        {
            throw new LoggerException("The environment variable TRACKED_FILES is required");
        }

        _trackedFiles.Clear();
        _trackedFiles.UnionWith(
            trackedFiles
                .Split(':', options: StringSplitOptions.RemoveEmptyEntries)
                .Select(TryGetFullPath)
                .Where(x => x is not null)
                .Select(x => x!)
        );
        eventSource.HandleProjectStarted(ProjectStarted);
    }

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.FullName : null;
        }
        catch
        {
            return null;
        }
    }

    [return: NotNullIfNotNull(nameof(path))]
    private static string? GetRelativePath(string? path, string? root = null)
    {
        if (path is null)
        {
            return null;
        }
        
        root = root ?? Environment.CurrentDirectory;
        path = Path.GetFullPath(path);
        path = Path.GetRelativePath(root, path);
        return "./" + path;
    }

    private ProjectInfo GetProjectSet(string key)
        => _projectFiles.GetOrAdd(key, file => new ProjectInfo(file));

    private void ProjectStarted(object sender, ProjectStartedEventArgs e)
    {
        if (e.Items == null || e.ProjectFile is null || !_trackedFiles.Contains(e.ProjectFile)) return;

        var files = GetProjectSet(e.ProjectFile);

        foreach (var item in e.Items.OfType<DictionaryEntry>())
        {
            if (item.Value is ITaskItem ii)
            {
                var fullPath = ii.GetMetadata("FullPath");
                if (!_trackedFiles.Contains(fullPath))
                    continue;
                files.Insert(item.Key as string, fullPath, ii);
            }
        }
    }

    public void Shutdown()
    {
        foreach (var (proj, files) in _projectFiles)
        {
            Generate(proj, files);
        }
    }

    private string Generate(string proj, ProjectInfo project)
    {
        var nixPath = Path.ChangeExtension(proj, ".nix");
        var projectFile = Path.GetFileName(proj);
        var projectName = Path.GetFileNameWithoutExtension(proj);
        var projectDir = Path.GetDirectoryName(proj)!;

        var referencesProps = new XElement("ItemGroup");
        foreach (var file in project.Projects)
        {
            var relative = GetRelativePath(file.Path);
            var refname = Path.GetFileNameWithoutExtension(file.Path);
            var reference = new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(file.Path)),
                new XElement("HintPath", $"${{projects.\"{relative}\"}}/lib/{refname}/{refname}.dll")
            );
            
            var remove = new XElement("Remove", new XAttribute("Include", file.Include));
            
            referencesProps.Add(reference);
            referencesProps.Add(remove);
        }

        var references = new XElement("Project", referencesProps);
        
        using var fileStream = new FileStream(nixPath, FileMode.Create);
        using var writer = new StreamWriter(fileStream);
        using var indented = new IndentedTextWriter(writer);

        indented.WriteLine("{");
        indented.Indent++;

        indented.WriteLine("stdenv,");
        indented.WriteLine("buildDotnetModule,");
        indented.WriteLine("projects,");
        indented.WriteLine("dotnetCorePackages,");
        indented.WriteLine("...");

        indented.Indent--;
        indented.WriteLine("} : buildDotnetModule {");
        indented.Indent++;

        indented.WriteLine($"pname = \"{projectName}\";");
        indented.WriteLine($"version = \"0.0.1\";");
        
        indented.WriteLine($"dotnet-sdk = dotnetCorePackages.sdk_8_0;");
        indented.WriteLine($"dotnet-runtime = dotnetCorePackages.runtime_8_0;");
        indented.WriteLine($"nugetDeps = ./deps.nix;");
        
        indented.WriteLine($"postPatch=''");
        indented.Indent++;

        indented.WriteLine($"cat << EOF > '{projectFile}.user'");

        indented.WriteLine(references.ToString(SaveOptions.DisableFormatting));
        
        indented.WriteLine("EOF");
        // indented.WriteLine("cat .nix-build.props");
        
        // foreach (var file in project.Projects)
        // {
        //     var refName = Path.GetFileNameWithoutExtension(file);
        //     indented.WriteLine($"dotnet remove reference {refName}");
        // }
        
        indented.Indent--;
        indented.WriteLine("'';");
        
        indented.WriteLine($"deps = [");
        indented.Indent++;

        indented.Indent--;
        indented.WriteLine($"];");

        indented.WriteLine($"projectFile = \"{project.ProjectRelativeFile}\";");

        indented.WriteLine($"src = [");
        indented.Indent++;

        foreach (var file in project.Files)
        {
            switch (Path.GetExtension(file))
            {
                case ".nix": continue;
            }
            
            var relative = Path.GetRelativePath(projectDir, file);
            if (!relative.StartsWith("../") && !relative.StartsWith("./"))
                relative = "./" + relative;
            indented.WriteLine(relative);
        }
        indented.WriteLine(project.ProjectRelativeFile);
        indented.WriteLine("./deps.nix");

        indented.Indent--;
        indented.WriteLine($"];");

        indented.WriteLine("unpackPhase = ''");
        indented.Indent++;

        indented.WriteLine("for srcFile in $src; do");
        indented.Indent++;

        indented.WriteLine("cp -v \"$srcFile\" \"$(stripHash \"$srcFile\")\"");

        indented.Indent--;
        indented.WriteLine("done");

        indented.Indent--;
        indented.WriteLine("'';");

        indented.Indent--;
        indented.WriteLine("}");

        return nixPath;
    }
}
