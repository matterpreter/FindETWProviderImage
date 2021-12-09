using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindETWProviderImage;
using System.Collections.Generic;
using System;
using System.Linq;

namespace FindETWProviderImageTests
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        [ExpectedException(typeof(System.IO.FileNotFoundException))]
        public void Invoke_InvalidSearchPathWithGuid_ThrowsException()
        {
            string[] Args =
            {
                "{FE4525E2-42DD-4583-80B1-24214BE944A2}",
                @"C:\oiwmjfwzpm.sys" // Bad path
            };

            Program.Main(Args);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Invoke_InvalidGuid_ThrowsException()
        {
            string[] Args =
            {
                "{BAD-GUID}", // Invalid GUID
                @"C:\Windows\System32\ntdll.dll"
            };

            Program.Main(Args);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Invoke_InvalidProviderName_ThrowsException()
        {
            string[] Args =
            {
                "This-Provider-DoesNot-Exist",
                @"C:\Windows\System32\ntdll.dll"
            };

            Program.Main(Args);
        }

        [TestMethod]
        public void Invoke_ValidProviderName_ReturnsResults()
        {
            string[] Args =
            {
                "Microsoft-Windows-TaskScheduler",
                @"C:\Windows\System32\schedsvc.dll"
            };

            Program.TotalReferences = 0;


            Program.Main(Args);

            Assert.IsTrue(Program.TotalReferences > 0); 
        }


        [TestMethod]
        public void GetAllFiles_ValidPath_ReturnsListSizeGreaterThanZero()
        {
            HashSet<string> Files = new();

            Files = Program.GetAllFiles(@"C:\Windows\System32");

            Assert.IsTrue(Files.Count > 0);
        }

        [TestMethod]
        public void ParseFile_ValidGuid_ReturnsOffset()
        {
            string KnownValidGuid = @"{f4e1897c-bb5d-5668-f1d8-040f4d8dd344}";
            Program.ProviderGuidBytes = Guid.Parse(KnownValidGuid).ToByteArray();
            string TargetFile = @"C:\Windows\System32\ntoskrnl.exe";
            Program.TotalReferences = 0;

            Program.ParseFile(TargetFile);

            Assert.IsTrue(Program.TotalReferences > 0);
        }

        [TestMethod]
        public void ParseFile_InvalidGuid_ReturnsNoOffsets()
        {
            string KnownInvalidGuid = @"{DF69F475-C47D-4EB6-B601-82AF67434DE1}";
            Program.ProviderGuidBytes = Guid.Parse(KnownInvalidGuid).ToByteArray();
            string TargetFile = @"C:\Windows\System32\ntoskrnl.exe";
            Program.TotalReferences = 0;


            Program.ParseFile(TargetFile);

            Assert.IsTrue(Program.TotalReferences == 0);
        }

        [TestMethod]
        public void Search_ValidGuid_ReturnsOffsetInArray()
        {
            string KnownValidGuid = @"{0000E73F-9BD4-4883-9DE6-C61B009F0C4F}";
            Program.ProviderGuidBytes = Guid.Parse(KnownValidGuid).ToByteArray();

            // Fill the array with bogus data
            Random random = new();
            byte[] MockFileBytes = new byte[1024];
            random.NextBytes(MockFileBytes);

            // Add the target GUID at index 32
            for (int i = 32; i < 32 + Program.ProviderGuidBytes.Length; i++)
            {
                MockFileBytes[i] = Program.ProviderGuidBytes[i-32];
            }

            List<int> Offsets = Program.BoyerMooreSearch(Program.ProviderGuidBytes, MockFileBytes);
            Assert.IsTrue(Offsets.Count > 0);
            Assert.IsTrue(Offsets.ElementAt(0) == 32);
        }

        [TestMethod]
        public void Search_InvalidGuid_ReturnsNoOffsets()
        {
            string KnownValidGuid = @"{FBF70DD4-93C8-491B-BAFA-E5D738DC6E1D}";
            Program.ProviderGuidBytes = Guid.Parse(KnownValidGuid).ToByteArray();

            // Create an array full of zeroes
            byte[] MockFileBytes = new byte[1024];

            List<int> Offsets = Program.BoyerMooreSearch(Program.ProviderGuidBytes, MockFileBytes);
            
            Assert.IsTrue(Offsets.Count == 0);
        }

        [TestMethod]
        public void TranslateProviderNameToGuid_ValidGuid_ReturnsProviderName()
        {
            string ProviderGuid;
            string ProviderName = "Microsoft-Windows-TaskScheduler";
            string ImagePath;

            ProviderGuid = Program.TranslateProviderNameToGuid(ProviderName, out ImagePath);

            Console.WriteLine("Name: " + ProviderName);
            Console.WriteLine("Guid: " + ProviderGuid);
            Console.WriteLine("Path: " + ImagePath);

            Assert.AreEqual(ProviderGuid, "{de7b24ea-73c8-4a09-985d-5bdadcfa9017}");
            //Assert.AreEqual(ImagePath.ToLower(), @"c:\windows\system32\schedsvc.dll");
        }

        [TestMethod]
        public void TranslateProviderNameToGuid_InvalidGuid_DoesNotReturnProviderName()
        {
            string ProviderGuid;
            string ProviderName = "Some-Invalid-Provider-Name";
            string ImagePath;

            ProviderGuid = Program.TranslateProviderNameToGuid(ProviderName, out ImagePath);

            Assert.AreEqual(ProviderGuid, string.Empty);
            Assert.AreEqual(ImagePath, string.Empty);
        }
    }
}
