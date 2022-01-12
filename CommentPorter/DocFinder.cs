using System;
using System.IO;

namespace CommentPorter
{
    public static class DocFinder
    {
        public static string DocsPath { get; set; }
        public static string DocsSource { get; set; }
        public static NamespaceMap NamespaceMap { get; set; }

        static string BuildDocPath(string docsRoot, string ns, string className)
        {
            var path = Path.Combine(docsRoot, ns ?? "", $"{className}.xml");
            return path;
        }

        static string MakeRelativePath(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (string.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath;
        }

        static readonly object sync = new object();

        public static string BuildRelativeDocPath(string className, string ns, string filePath)
        {
            var docsRoot = DocsPath;
            var docFilePath = BuildDocPath(docsRoot, NamespaceMap.ToDestination(ns), className);

            if (docFilePath == null) 
            {
                return null;
            }

            lock (sync)
            {
                if (!File.Exists(docFilePath))
                {
                    // See if the file exists in the source folder
                    var sourcePath = BuildDocPath(DocsSource, NamespaceMap.ToSource(ns), className);
                    if (File.Exists(sourcePath))
                    {
                        var destinationDir = Path.GetDirectoryName(docFilePath);
                        if (!Directory.Exists(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        // Copy the file to the local docs path
                        File.Copy(sourcePath, docFilePath, false);

                        // Now we need to fix up the namespaces
                        string content = File.ReadAllText(docFilePath);
                        content = content.Replace(NamespaceMap.FormsRoot, NamespaceMap.Root);
                        File.WriteAllText(docFilePath, content);
                    }
                    else
                    {
                        // There's no source documentation to link to
                        return null;
                    }
                }
            }

            return MakeRelativePath(filePath, docFilePath);
        }
    }

    public class NamespaceMap
    {
        public string Root { get; }
        public const string FormsRoot = "Xamarin.Forms";

        public NamespaceMap(string root)
        {
            Root = root;
        }

        public string ToDestination(string ns)
        {
            if (ns.StartsWith(Root)) 
            { 
                return ns; 
            }

            if (!ns.StartsWith(FormsRoot))
            {
                return null;
            }

            return Root + ns.Substring(FormsRoot.Length);
        }

        public string ToSource(string ns) 
        {
            if (ns.StartsWith(FormsRoot))
            {
                return ns;
            }

            if (!ns.StartsWith(Root)) 
            {
                return null;
            }

            return FormsRoot + ns.Substring(Root.Length);
        }
    }
}
