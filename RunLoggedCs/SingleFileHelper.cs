using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RunLoggedCs;

public static class SingleFileHelper
{
    [SuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file")]
    public static CSharpCompilation AddAssemblyReference(this CSharpCompilation ctx, Assembly assy)
    {
        if (!string.IsNullOrEmpty(assy.Location))
            return ctx.AddReferences(MetadataReference.CreateFromFile(assy.Location));

        var entryPath = Process.GetCurrentProcess().MainModule.FileName;

        byte[] bundleSignature =
        {
            // 32 bytes represent the bundle signature: SHA-256 for ".net core bundle"
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
            0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
            0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
        };

        using var memoryMappedPackage = MemoryMappedFile.CreateFromFile(entryPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var packageView = memoryMappedPackage.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        int position = SearchInFile(packageView, bundleSignature);
        if (position == -1)
            throw new Exception("placeholder not found");

        long headerOffset = packageView.ReadInt64(position - sizeof(long));

        var manifest = ReadManifest(packageView, headerOffset);

        var entry = manifest.Entries.OrderBy(e => e.RelativePath.Length)
            .First(e => e.Type == FileType.Assembly && e.RelativePath.Contains(assy.GetName().Name));
        var stream = new UnmanagedMemoryStream(packageView.SafeMemoryMappedViewHandle, entry.Offset, entry.Size);
        return ctx.AddReferences(MetadataReference.CreateFromStream(stream));
    }

    internal static unsafe int SearchInFile(MemoryMappedViewAccessor accessor, byte[] searchPattern)
    {
        var safeBuffer = accessor.SafeMemoryMappedViewHandle;
        return KMPSearch(searchPattern, (byte*)safeBuffer.DangerousGetHandle(), (int)safeBuffer.ByteLength);
    }

    // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
    private static int[] ComputeKMPFailureFunction(byte[] pattern)
    {
        int[] table = new int[pattern.Length];
        if (pattern.Length >= 1)
        {
            table[0] = -1;
        }

        if (pattern.Length >= 2)
        {
            table[1] = 0;
        }

        int pos = 2;
        int cnd = 0;
        while (pos < pattern.Length)
        {
            if (pattern[pos - 1] == pattern[cnd])
            {
                table[pos] = cnd + 1;
                cnd++;
                pos++;
            }
            else if (cnd > 0)
            {
                cnd = table[cnd];
            }
            else
            {
                table[pos] = 0;
                pos++;
            }
        }

        return table;
    }

    // See: https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm
    private static unsafe int KMPSearch(byte[] pattern, byte* bytes, long bytesLength)
    {
        int m = 0;
        int i = 0;
        int[] table = ComputeKMPFailureFunction(pattern);

        while (m + i < bytesLength)
        {
            if (pattern[i] == bytes[m + i])
            {
                if (i == pattern.Length - 1)
                {
                    return m;
                }

                i++;
            }
            else
            {
                if (table[i] > -1)
                {
                    m = m + i - table[i];
                    i = table[i];
                }
                else
                {
                    m++;
                    i = 0;
                }
            }
        }

        return -1;
    }

    public struct Header
    {
        public uint MajorVersion;
        public uint MinorVersion;
        public int FileCount;
        public string BundleID;

        // Fields introduced with v2:
        public long DepsJsonOffset;
        public long DepsJsonSize;
        public long RuntimeConfigJsonOffset;
        public long RuntimeConfigJsonSize;
        public ulong Flags;

        public ImmutableArray<Entry> Entries;
    }

    /// <summary>
    /// FileType: Identifies the type of file embedded into the bundle.
    ///
    /// The bundler differentiates a few kinds of files via the manifest,
    /// with respect to the way in which they'll be used by the runtime.
    /// </summary>
    public enum FileType : byte
    {
        Unknown, // Type not determined.
        Assembly, // IL and R2R Assemblies
        NativeBinary, // NativeBinaries
        DepsJson, // .deps.json configuration file
        RuntimeConfigJson, // .runtimeconfig.json configuration file
        Symbols // PDB Files
    };

    public struct Entry
    {
        public long Offset;
        public long Size;
        public long CompressedSize; // 0 if not compressed, otherwise the compressed size in the bundle
        public FileType Type;
        public string RelativePath; // Path of an embedded file, relative to the Bundle source-directory.
    }

    static UnmanagedMemoryStream AsStream(MemoryMappedViewAccessor view)
    {
        long size = checked((long)view.SafeMemoryMappedViewHandle.ByteLength);
        return new UnmanagedMemoryStream(view.SafeMemoryMappedViewHandle, 0, size);
    }

    public static Header ReadManifest(MemoryMappedViewAccessor view, long bundleHeaderOffset)
    {
        using var stream = AsStream(view);
        stream.Seek(bundleHeaderOffset, SeekOrigin.Begin);
        return ReadManifest(stream);
    }

    public static Header ReadManifest(Stream stream)
    {
        var header = new Header();
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        header.MajorVersion = reader.ReadUInt32();
        header.MinorVersion = reader.ReadUInt32();

        // Major versions 3, 4 and 5 were skipped to align bundle versioning with .NET versioning scheme
        if (header.MajorVersion < 1 || header.MajorVersion > 6)
        {
            throw new InvalidDataException($"Unsupported manifest version: {header.MajorVersion}.{header.MinorVersion}");
        }

        header.FileCount = reader.ReadInt32();
        header.BundleID = reader.ReadString();
        if (header.MajorVersion >= 2)
        {
            header.DepsJsonOffset = reader.ReadInt64();
            header.DepsJsonSize = reader.ReadInt64();
            header.RuntimeConfigJsonOffset = reader.ReadInt64();
            header.RuntimeConfigJsonSize = reader.ReadInt64();
            header.Flags = reader.ReadUInt64();
        }

        var entries = ImmutableArray.CreateBuilder<Entry>(header.FileCount);
        for (int i = 0; i < header.FileCount; i++)
        {
            entries.Add(ReadEntry(reader, header.MajorVersion));
        }

        header.Entries = entries.MoveToImmutable();
        return header;
    }

    private static Entry ReadEntry(BinaryReader reader, uint bundleMajorVersion)
    {
        Entry entry;
        entry.Offset = reader.ReadInt64();
        entry.Size = reader.ReadInt64();
        entry.CompressedSize = bundleMajorVersion >= 6 ? reader.ReadInt64() : 0;
        entry.Type = (FileType)reader.ReadByte();
        entry.RelativePath = reader.ReadString();
        return entry;
    }
}
