# FindETWProviderImage
Quickly search for references to a GUID in DLLs, EXEs, and drivers

## Usage:
```
.\FindETWProviderImage.exe "your-guid-here" "\path\to\search\directory"
```
https://user-images.githubusercontent.com/1756781/145394106-dd3e204c-fbaa-4b9a-8037-e3eac5d1c11e.mp4

## What Next?
Since the tool is only returning basic offsets/RVAs, you'll still need to disassemble the image in Ghidra/IDA/etc.  
My workflow is to load the image into the disassembler, do the initial automatic analysis, and then look for cross-references to the offset/RVA, specifically ones coming from `EventRegister()` (user mode) and `EtwRegister()` (kernel mode).

![](https://user-images.githubusercontent.com/1756781/145055293-a8967d22-32c4-4744-bc8a-f3c16c570950.png)

## To Do:
- [X] Add checks for `EventRegister()` and `EtwRegister()` to help identify providers
- [ ] Add provider name to GUID resolution functionality

## How it Works
1. Recursively search the supplied directory for files ending with `.dll`, `.exe`, or `.sys`
2. Use a [Boyer-Moore search](https://en.wikipedia.org/wiki/Boyer%E2%80%93Moore_string-search_algorithm) to parse each of the files for the target GUID
3. If references are found in the image, return the offset and relative virtual address (RVA) of each reference, as well as if the ETW registration functions `EtwRegister()` or `EventRegister()` were found in the imports table

## Credits
Thanks to Matt Graeber ([@mattifestation](https://twitter.com/mattifestation)) for the original idea of identifying provider images by locating GUIDs inside the files
