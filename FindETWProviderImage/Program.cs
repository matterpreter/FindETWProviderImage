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
        private static readonly string Usage = "FindETWProviderImage.exe {provider-guid} <search_path>";
        private static readonly string[] FileExtensions = { ".dll", ".exe", ".sys" };
        private static byte[] ProviderGuidBytes;

        public struct ProviderImage
        {
            public string FilePath = string.Empty;
            public uint Offset = 0;
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

                if ((File.GetAttributes(SearchRoot) & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    // Search target is a directory, start recursively searching
                }
                else
                {
                    Console.WriteLine($"Target File: {SearchRoot}");
                    Console.WriteLine($"GUID: {TargetGuid}");

                    Stopwatch sw = Stopwatch.StartNew();

                    ParseSingleFile(SearchRoot, ProviderGuidBytes);

                    sw.Stop();
                    Console.WriteLine($"\nTime Elapsed: {sw.ElapsedMilliseconds} milliseconds");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void ParseSingleFile(string FilePath, byte[] ProviderGuid)
        {
            byte[] FileBytes = File.ReadAllBytes(FilePath);

            List<int> Offsets = BoyerMooreSearch(ProviderGuidBytes, FileBytes);
            Console.WriteLine($"Found {Offsets.Count} references");
            
            if (Offsets.Count > 0)
            {
                foreach (var Offset in Offsets)
                {
                    Console.WriteLine("  {0}) Offset: 0x{1:x} RVA: 0x{2:x}",
                        Offsets.IndexOf(Offset)+1,
                        Offset,
                        OffsetToRVA(FileBytes, Offset));
                }
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
    }
}
