﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace FindETWProviderImage
{
    public class Program
    {
        private static readonly string Usage = 
            "FindETWProviderImage.exe \"<{provider-guid}|Provider-Name>\" \"<search_path>\"";

        private static string TargetGuid;
        public static byte[] ProviderGuidBytes;
        private static HashSet<string> TargetFiles = new();
        public static int TotalReferences = 0;

        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine(Usage);
                return;
            }

            if (!Directory.Exists(args[1]) && !File.Exists(args[1]))
            {
                throw new FileNotFoundException();
            }

            if (Guid.TryParse(args[0], out _))
            {
                TargetGuid = args[0];
            }
            else
            {
                // If args[0] isn't a GUID, it has to be a provider name
                string ImagePathFromRegistry;
                TargetGuid = TranslateProviderNameToGuid(args[0], out ImagePathFromRegistry);

                if (string.IsNullOrEmpty(TargetGuid))
                {
                    throw new ArgumentException("The provider name or GUID does not appear to be valid");
                }
                else
                {
                    Console.WriteLine($"Translated {args[0]} to {TargetGuid}");
                    if (!string.IsNullOrEmpty(ImagePathFromRegistry))
                    {
                        Console.WriteLine($"Found provider in the registry: {ImagePathFromRegistry}\n");
                    }
                }
            }
            
            Stopwatch sw = Stopwatch.StartNew();

            ProviderGuidBytes = Guid.Parse(TargetGuid).ToByteArray();
            string SearchRoot = args[1];

            // Check if the search target is a directory
            if ((File.GetAttributes(SearchRoot) & FileAttributes.Directory) == FileAttributes.Directory)
            {
                // Create a list of files to check by recursively searching the target directory
                TargetFiles = GetAllFiles(SearchRoot);
                Console.WriteLine($"Searching {TargetFiles.Count} files for {TargetGuid}...\n");

                // Iterate over the collection of files
                Parallel.ForEach(TargetFiles, file => ParseFile(file/*, ProviderGuidBytes*/));
            }
            else
            {
                // Target is a single file
                ParseFile(SearchRoot/*, ProviderGuidBytes*/);
            }

            Console.WriteLine($"Total References: {TotalReferences}");
            sw.Stop();
            Console.WriteLine($"Time Elapsed: {sw.ElapsedMilliseconds / 1000.0000} seconds");
        }

        public static void ParseFile(string FilePath/*, byte[] ProviderGuid*/)
        {
            byte[] FileBytes = File.ReadAllBytes(FilePath);

            List<int> Offsets = BoyerMooreSearch(ProviderGuidBytes, FileBytes);

            if (Offsets.Count > 0)
            {
                Console.WriteLine("Target File: {0}\n" +
                            "Registration Function Imported: {1}\n" +
                            "Found {2} reference{3}:",
                            FilePath,
                            DoesImageImportEventRegistrationAPI(FilePath) ? "True" : "False",
                            Offsets.Count,
                            Offsets.Count > 1 ? "s" : "");

                foreach (var Offset in Offsets)
                {
                    string SectionName;

                    Console.WriteLine("  {0}) Offset: 0x{1:x} RVA: 0x{2:x}{3}",
                        Offsets.IndexOf(Offset) + 1,
                        Offset,
                        OffsetToRVA(FileBytes, Offset, out SectionName),
                        string.IsNullOrEmpty(SectionName) ? "" : string.Format(" ({0})", SectionName));
                }
                Console.WriteLine();
                TotalReferences += Offsets.Count;
            }
        }

        public static List<int> BoyerMooreSearch(byte[] ProviderGuid, byte[] FileBytes)
        {
            List<int> Offsets = new();

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

        static int OffsetToRVA(byte[] FileBytes, int Offset, out string SectionName)
        {
            MemoryStream stream = new(FileBytes);
            PEReader reader = new(stream);

            var SectionHeaders = reader.PEHeaders.SectionHeaders;
            foreach (SectionHeader Header in SectionHeaders)
            {
                if (Offset > Header.VirtualAddress && 
                    Offset < Header.VirtualAddress + Header.VirtualSize)
                {
                    SectionName = Header.Name;
                    return Offset + Header.VirtualAddress - Header.PointerToRawData;
                }
            }

            SectionName = SectionHeaders.ElementAt(0).Name;
            return Offset;
        }

        public static HashSet<string> GetAllFiles(string SearchDirectory)
        {
            EnumerationOptions Options = new()
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };

            return Directory.EnumerateFiles(SearchDirectory, "*.*", Options).Where(
                s => s.EndsWith(".dll") || s.EndsWith(".exe") || s.EndsWith(".sys"))
                .ToHashSet();
        }

        static bool DoesImageImportEventRegistrationAPI(string FilePath)
        {
            PeNet.PeFile pe = new PeNet.PeFile(FilePath);
            bool Found = false;

            if (pe.ImportedFunctions != null)
            {
                foreach (var Function in pe.ImportedFunctions)
                {
                    if (Function.Name == "EventRegister" || Function.Name == "EtwRegister")
                    {
                        Found = true;
                        break;
                    }
                }
            }

            return Found;
            
        }
        public static string TranslateProviderNameToGuid(string ProviderName, out string ImagePath)
        {
            ImagePath = string.Empty;

            // Check #1 - https://docs.microsoft.com/en-us/windows/win32/wes/identifying-the-provider
            using (RegistryKey RegKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\WINEVT\Publishers", false))
            {
                foreach (string SubKeyName in RegKey.GetSubKeyNames())
                {
                    RegistryKey RegSubKey = RegKey.OpenSubKey(SubKeyName, false);
                    string DefaultVal = RegSubKey.GetValue("").ToString();

                    if (DefaultVal.Equals(ProviderName, StringComparison.OrdinalIgnoreCase))
                    {
                        ImagePath = NormalizePath(RegSubKey.GetValue("ResourceFileName").ToString());
                        return SubKeyName;
                    }
                }
            }

            // Check #2 - https://stackoverflow.com/a/61100379
            using (RegistryKey RegKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\WINEVT\Channels", false))
            {
                foreach (string SubKeyName in RegKey.GetSubKeyNames())
                {
                    if (SubKeyName.StartsWith(ProviderName, StringComparison.OrdinalIgnoreCase))
                    {
                        RegistryKey SubKey = RegKey.OpenSubKey(SubKeyName,false);
                        return SubKey.GetValue("OwningPublisher").ToString();
                    }
                }
            }

            return string.Empty;
        }

        public static string NormalizePath(string BasePath)
        {
            // https://stackoverflow.com/a/21058121
            return Path.GetFullPath(new Uri(BasePath).LocalPath)
               .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
               //.ToUpperInvariant();
        }
    }
}
