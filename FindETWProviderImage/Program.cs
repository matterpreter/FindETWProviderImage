using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace FindETWProviderImage
{
    internal class Program
    {
        private static readonly string Usage = "FindETWProviderImage.exe \"{provider-guid}\" \"<search_path>\"";
        private static byte[] ProviderGuidBytes;

        public struct Reference
        {
            public int Offset;
            public int RVA;
        }
        public struct ProviderImage
        {
            public string FilePath = string.Empty;
            public List<Reference> References = new List<Reference>();
        }

        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.WriteLine(Usage);
                    return;
                }
                if (!Guid.TryParse(args[0], out _))
                {
                    throw new ArgumentException("The GUID provided does not appear to be valid");
                }
                if (!Directory.Exists(args[1]) && !File.Exists(args[1]))
                {
                    throw new FileNotFoundException("The target file or directory does not exist");
                }

                string TargetGuid = args[0];
                ProviderGuidBytes = Guid.Parse(args[0]).ToByteArray();
                string SearchRoot = args[1];

                // Check if the search target is a directory
                if ((File.GetAttributes(SearchRoot) & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    // Build the list of files
                    List<string> TargetFiles = GetAllFiles(SearchRoot);
                    Console.WriteLine($"Searching {TargetFiles.Count} files for {TargetGuid}...");

                    foreach (string TargetFile in TargetFiles)
                    {
                        try
                        {
                            if (TargetFile == @"C:\Windows\System32\ntoskrnl.exe")
                            {
                                // For testing
                            }
                            ProviderImage Image = ParseSingleFile(SearchRoot, ProviderGuidBytes);
                            Console.WriteLine($"Target File: {Image.FilePath}\n" +
                                $"GUID: {TargetGuid}\n" +
                                $"Found {Image.References.Count} references:");
                            foreach (Reference reference in Image.References)
                            {
                                Console.WriteLine($"  {Image.References.IndexOf(reference) + 1}) Offset: 0x{reference.Offset:x} RVA: 0x{reference.RVA:x}");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex is DirectoryNotFoundException)
                            {
                                Console.WriteLine($"Couldn't access {TargetFile}");
                            }
                            continue;
                        }
                        //if (ParseSingleFile(TargetFile, ProviderGuidBytes))
                        //{
                        //    return;
                        //}
                    }
                }
                else
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    try
                    {
                        ProviderImage Image = ParseSingleFile(SearchRoot, ProviderGuidBytes);
                        Console.WriteLine($"Target File: {Image.FilePath}\n" +
                            $"GUID: {TargetGuid}\n" + 
                            $"Found {Image.References.Count} references:");
                        foreach (Reference reference in Image.References)
                        {
                            Console.WriteLine($"  {Image.References.IndexOf(reference)+1}) Offset: 0x{reference.Offset:x} RVA: 0x{reference.RVA:x}");
                        }
                    }
                    catch (ProviderNotFoundException)
                    {
                        Console.WriteLine("Found no reference to the GUID in the target file");
                    }

                    sw.Stop();
                    Console.WriteLine($"\nTime Elapsed: {sw.ElapsedMilliseconds} milliseconds");
                }
            }
            catch (Exception ) 
            {
                //
            }
        }

        public class ProviderNotFoundException : Exception { }

        static ProviderImage ParseSingleFile(string FilePath, byte[] ProviderGuid)
        {
            byte[] FileBytes = File.ReadAllBytes(FilePath);

            List<int> Offsets = BoyerMooreSearch(ProviderGuidBytes, FileBytes);
            //Console.WriteLine($"Found {Offsets.Count} references");
            
            if (Offsets.Count > 0)
            {
                ProviderImage Provider = new ProviderImage() 
                { 
                    FilePath = FilePath
                };

                foreach (var Offset in Offsets)
                {
                    Provider.References.Add(new Reference() { Offset = Offset, RVA = OffsetToRVA(FileBytes, Offset) });
                    //Console.WriteLine("  {0}) Offset: 0x{1:x} RVA: 0x{2:x}",
                    //    Offsets.IndexOf(Offset)+1,
                    //    Offset,
                    //    OffsetToRVA(FileBytes, Offset));
                }

                return Provider;
            }
            else
            {
                throw new ProviderNotFoundException();
            }

        }

        static List<int> BoyerMooreSearch(byte[] ProviderGuid, byte[] FileBytes)
        {
            List<int> Offsets = new List<int>();

            int[] Alphabet = new int[256];
            for (int i = 0; i < Alphabet.Length; i++) 
            {
                Alphabet[i] = ProviderGuid.Length; 
            }

            for (int i = 0; i < ProviderGuid.Length; i++)
            {
                Alphabet[ProviderGuid[i]] = ProviderGuid.Length - i - 1;
            }

            int Index = ProviderGuid.Length - 1;
            var lastByte = ProviderGuid.Last();

            while (Index < FileBytes.Length)
            {
                byte CheckByte = FileBytes[Index];
                if (FileBytes[Index] == lastByte)
                {
                    bool Found = true;
                    for (int j = ProviderGuid.Length - 2; j >= 0; j--)
                    {
                        if (FileBytes[Index - ProviderGuid.Length + j + 1] != ProviderGuid[j])
                        {
                            Found = false;
                            break;
                        }
                    }

                    if (!Found)
                    {
                        Index++;
                    }
                    else
                    {
                        Offsets.Add(Index - ProviderGuid.Length + 1);
                        Index++;
                    }
                        
                }
                else
                {
                    Index += Alphabet[CheckByte];
                }
            }
            
            return Offsets;
        }

        static int OffsetToRVA(byte[] FileBytes, int Offset)
        {
            MemoryStream stream = new MemoryStream(FileBytes);
            PEReader reader = new PEReader(stream);

            var SectionHeaders = reader.PEHeaders.SectionHeaders;
            foreach (SectionHeader Header in SectionHeaders)
            {
                if (Offset > Header.VirtualAddress && Offset < Header.VirtualAddress + Header.VirtualSize)
                {
                    return Offset + Header.PointerToRawData;
                }
            }

            return 0;
        }

        static List<string> GetAllFiles(string SearchDirectory)
        {
            EnumerationOptions Options = new EnumerationOptions()
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            List<string> AllFiles = new List<string>(Directory.EnumerateFiles(SearchDirectory, "*.*", Options)
                .Where(s => s.EndsWith(".dll") || s.EndsWith(".exe") || s.EndsWith(".sys")));

            return AllFiles;            
        }
    }
}
