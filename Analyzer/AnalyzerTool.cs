using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityDataTools.Analyzer.SQLite;
using UnityDataTools.FileSystem;

namespace UnityDataTools.Analyzer;

public class AnalyzerTool
{
    public int Analyze(string path, string databaseName, string searchPattern, bool skipReferences)
    {
        using SQLiteWriter writer = new(databaseName, skipReferences);

        try
        {
            writer.Begin();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error creating database: {e.Message}");
            return 1;
        }

        var timer = new Stopwatch();
        timer.Start();

        var files = Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories);
        int i = 1;
        int lastLength = 0;
        foreach (var file1 in files)
        {
            var file = file1;
            try
            {
                long unityFsOffset = UnityFS_Offset(file);
                if (unityFsOffset == -1)
                    continue;
                if (unityFsOffset > 0)
                {
                    string destFileName = file + "_";

                    CopyFileByOffset(file, unityFsOffset, destFileName);

                    file = destFileName;
                }

                UnityArchive archive = null;

                string containingFolder = Path.GetDirectoryName(file);
                try
                {
                    archive = UnityFileSystem.MountArchive(file, "archive:" + Path.DirectorySeparatorChar);
                }
                catch (NotSupportedException)
                {
                    // It wasn't an AssetBundle, try to open the file as a SerializedFile.

                    var relativePath = Path.GetRelativePath(path, file);

                    Console.Write($"\rProcessing {i * 100 / files.Length}% ({i}/{files.Length}) {file}");

                    writer.WriteSerializedFile(relativePath, file, containingFolder);
                }

                if (archive != null)
                {
                    try
                    {
                        var assetBundleName = Path.GetRelativePath(path, file);

                        writer.BeginAssetBundle(assetBundleName, new FileInfo(file).Length);

                        var message = $"Processing {i * 100 / files.Length}% ({i}/{files.Length}) {assetBundleName}";
                        Console.Write($"\r{message}{new string(' ', Math.Max(0, lastLength - message.Length))}");
                        lastLength = message.Length;

                        foreach (var node in archive.Nodes)
                        {
                            if (node.Flags.HasFlag(ArchiveNodeFlags.SerializedFile))
                            {
                                try
                                {
                                    writer.WriteSerializedFile(node.Path, "archive:/" + node.Path, containingFolder);
                                }
                                catch (Exception e)
                                {
                                    Console.Error.WriteLine();
                                    Console.Error.WriteLine($"Error processing {node.Path} in archive {file}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        writer.EndAssetBundle();
                        archive.Dispose();
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Error processing file {file}!");
                Console.Write($"{e.GetType()}: ");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            
            string destFile = file1 + "_";
            if(File.Exists(destFile))
                File.Delete(destFile);

            ++i;
        }
        Console.WriteLine();
        Console.WriteLine($"Finalizing database... {databaseName}");
        Console.WriteLine();

        writer.End();

        timer.Stop();
        Console.WriteLine();
        Console.WriteLine($"Total time: {(timer.Elapsed.TotalMilliseconds / 1000.0):F3} s");

        return 0;
    }

    private static void CopyFileByOffset(string srcFile, long startOffset, string destFile)
    {
        using (FileStream src = File.Open(srcFile, FileMode.Open))
        {
            FileStream dst = File.Open(destFile, FileMode.OpenOrCreate);
            src.Position = startOffset;
            src.CopyTo(dst);
            dst.Flush();
            dst.Close();
        }
    }

    private static long UnityFS_Offset(string file)
    {
        //UnityFS
        using (FileStream sr = File.Open(file, FileMode.Open))
        {
            //This is an arbitrary size for this example.
            string unityfs = "UnityFS";
            int unityfsLength = unityfs.Length;
            Queue<char> Header = new Queue<char>(unityfs.ToCharArray());
            while (true)
            {
                if (Header.Count == 0)
                    return sr.Position-unityfsLength;

                char headChar = Header.Peek();

                int readChar = sr.ReadByte();
                if (readChar == -1)
                    return -1;
                
                if (readChar == headChar)
                    Header.Dequeue();
                else
                {
                    
                    if(Header.Count!=unityfsLength)
                        Header = new Queue<char>(unityfs.ToCharArray()); //reset
                }
            }
        }

        return -1;
    }
}