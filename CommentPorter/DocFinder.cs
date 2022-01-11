using System;
using System.IO;

namespace CommentPorter
{
    public static class DocFinder 
    {
        static string BuildDocPath (string docsRoot, string className) 
        {
            var path = Path.Combine(docsRoot, $"{className}.xml");
            return path;
        }

        static string FindDocsRoot(string classPath) 
        {
            var startDir = Path.GetDirectoryName(classPath);
            string docs;

            do
            {
                startDir = Path.GetDirectoryName(startDir);

                if (string.IsNullOrEmpty(startDir)) 
                {
                    throw new Exception("No docs folder found.");
                }

                docs = Path.Combine(startDir, "docs");
            }
            while (!Directory.Exists(docs));

            return docs;
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

        public static string BuildRelativeDocPath(string className, string classPath) 
        { 
            var docsRoot = FindDocsRoot(classPath);
            var docPath = BuildDocPath(docsRoot, className);

            if (!File.Exists(docPath)) 
            {
                return null;
            }

            return MakeRelativePath(classPath, docPath);
        }
    }
}
