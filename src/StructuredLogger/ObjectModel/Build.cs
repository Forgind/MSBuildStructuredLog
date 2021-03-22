using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Microsoft.Build.Logging.StructuredLogger
{
    /// <summary>
    /// Class representation of an MSBuild overall build execution.
    /// </summary>
    public class Build : TimedNode
    {
        public StringCache StringTable { get; } = new StringCache();

        public bool IsAnalyzed { get; set; }
        public bool Succeeded { get; set; }

        public string LogFilePath { get; set; }
        public int FileFormatVersion { get; set; }
        public byte[] SourceFilesArchive { get; set; }

        private Dictionary<string, ArchiveFile> sourceFiles;
        public Dictionary<string, ArchiveFile> SourceFiles
        {
            get
            {
                if (sourceFiles == null)
                {
                    lock (this)
                    {
                        if (sourceFiles == null)
                        {
                            sourceFiles = new Dictionary<string, ArchiveFile>();
                            if (SourceFilesArchive != null)
                            {
                                var files = ReadSourceFiles(SourceFilesArchive);
                                sourceFiles = files.ToDictionary(file => file.FullPath);
                            }
                        }
                    }
                }

                return sourceFiles;
            }
        }

        private NamedNode evaluationFolder;
        public NamedNode EvaluationFolder
        {
            get
            {
                if (evaluationFolder == null)
                {
                    evaluationFolder = GetOrCreateNodeWithName<TimedNode>(Strings.Evaluation);
                }

                return evaluationFolder;
            }
        }

        public static IReadOnlyList<ArchiveFile> ReadSourceFiles(byte[] sourceFilesArchive)
        {
            using (var stream = new MemoryStream(sourceFilesArchive))
            {
                return ReadSourceFiles(stream);
            }
        }

        public static IReadOnlyList<ArchiveFile> ReadSourceFiles(string zipFullPath)
        {
            using (var stream = new FileStream(zipFullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return ReadSourceFiles(stream);
            }
        }

        public static IReadOnlyList<ArchiveFile> ReadSourceFiles(Stream stream)
        {
            var result = new List<ArchiveFile>();

            try
            {
                using (var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in zipArchive.Entries)
                    {
                        var file = ArchiveFile.From(entry);
                        result.Add(file);
                    }
                }
            }
            catch
            {
                // The archive is likely incomplete (corrupt) because the build crashed.
                // Tolerate this situation.
            }

            return result;
        }

        public BuildStatistics Statistics { get; set; } = new BuildStatistics();

        public Dictionary<string, HashSet<string>> TaskAssemblies { get; } = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterTask(Task task)
        {
            if (task.FromAssembly is string assemblyPath)
            {
                lock (TaskAssemblies)
                {
                    if (!TaskAssemblies.TryGetValue(assemblyPath, out var bucket))
                    {
                        bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        TaskAssemblies[assemblyPath] = bucket;
                    }

                    bucket.Add(task.Name);
                }
            }
        }

        public override string TypeName => nameof(Build);

        public override string ToString() => $"Build {(Succeeded ? "succeeded" : "failed")}. Duration: {this.DurationText}";

        public TreeNode FindDescendant(int index)
        {
            int current = 0;
            var cts = new CancellationTokenSource();
            TreeNode found = default;
            VisitAllChildren<TimedNode>(node =>
            {
                if (current == index)
                {
                    found = node;
                    cts.Cancel();
                }
                current++;
            }, cts.Token);

            return found;
        }

        public ProjectEvaluation FindEvaluation(int id)
        {
            var evaluation = EvaluationFolder;
            if (evaluation == null)
            {
                return null;
            }

            var child = evaluation.FindChild<ProjectEvaluation>(e => e.Id == id);
            return child;
        }
    }
}
