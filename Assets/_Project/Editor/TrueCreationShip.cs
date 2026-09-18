using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrueEngine.EditorTools
{
    /// <summary>
    /// The file work of True Creation's after-build step, kept free of Unity so it can be tested on its own: copy a
    /// listed set of files into the build, take out of the build output what the exe does not use, clean what names the
    /// PC that built it or the Unity account it was built under, check that nothing still does, and zip the finished
    /// folder. Only the build output is changed; the project's files are read, never written.
    /// </summary>
    internal static class TrueCreationShip
    {
        public sealed class CopyResult
        {
            public int Files;
            public long Bytes;
            public readonly List<string> Missing = new List<string>();
            public int LeftOutFiles;
            public long LeftOutBytes;
        }

        public sealed class ZipResult
        {
            public string ZipPath;
            public int Files;
            public long Bytes;
            public long ZipBytes;
        }

        /// <summary>The text files the after-build step cleans and checks (the same kinds the secret scan reads).</summary>
        public static readonly string[] TextExtensions = { ".json", ".jsonl", ".md", ".txt", ".tsv", ".ini", ".lua", ".csv" };

        public static bool IsText(string path) => TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>
        /// Copies the listed files (relative to src, '/' separated) to the same places under dst. Also counts what src
        /// holds that is not listed, so the build can say what stayed in the project.
        /// </summary>
        public static CopyResult CopyListed(string src, string dst, IEnumerable<string> relativePaths)
        {
            var result = new CopyResult();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in relativePaths)
            {
                var native = rel.Replace('/', Path.DirectorySeparatorChar);
                listed.Add(native);
                var from = Path.Combine(src, native);
                if (!File.Exists(from)) { result.Missing.Add(rel); continue; }
                var to = Path.Combine(dst, native);
                Directory.CreateDirectory(Path.GetDirectoryName(to) ?? dst);
                File.Copy(from, to, true);
                result.Files++;
                result.Bytes += new FileInfo(from).Length;
            }
            if (Directory.Exists(src))
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    if (listed.Contains(file.Substring(src.Length).TrimStart('\\', '/'))) continue;
                    result.LeftOutFiles++;
                    result.LeftOutBytes += new FileInfo(file).Length;
                }
            return result;
        }

        /// <summary>
        /// Takes the named files or folders (directly under root, which must be the build's StreamingAssets) out of the
        /// build output. The project keeps its own copies; Unity puts them back on the next build and this runs again.
        /// </summary>
        public static List<string> RemoveFromBuildOutput(string root, IEnumerable<string> names)
        {
            var removed = new List<string>();
            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (Directory.Exists(path)) { Directory.Delete(path, true); removed.Add(name + "/"); }
                else if (File.Exists(path)) { File.Delete(path); removed.Add(name); }
            }
            return removed;
        }

        // ------------------------------------------------------------------ what names the PC that built it

        /// <summary>
        /// This PC's user folder as paths write it (backslashes, forward slashes, JSON-escaped), any letter case, and only
        /// where the folder name ends: C:\Users\name\... matches, C:\Users\nameXYZ does not.
        /// </summary>
        public static Regex UserFolderPattern(string profile)
        {
            var variants = new[] { profile, profile.Replace('\\', '/'), profile.Replace("\\", "\\\\") }
                .Distinct().OrderByDescending(v => v.Length).Select(Regex.Escape);
            return new Regex("(?:" + string.Join("|", variants) + @")(?![A-Za-z0-9_.\-])", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// A person's name as notes write it: the whole word, capitalised as given. Null for a name that is too short or
        /// not a plain word, which could not be replaced safely.
        /// </summary>
        public static Regex NamePattern(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || !name.All(char.IsLetter)) return null;
            return new Regex(@"\b" + Regex.Escape(name) + @"\b");
        }

        /// <summary>
        /// Replaces every match in the text files under root (read as UTF-8; a byte-order mark is kept as found).
        /// Returns { files changed, replacements }.
        /// </summary>
        public static int[] ReplaceInText(string root, Regex pattern, string replacement)
        {
            int files = 0, hits = 0;
            if (!Directory.Exists(root)) return new[] { 0, 0 };
            var literal = (replacement ?? "").Replace("$", "$$");
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(IsText))
            {
                var bytes = File.ReadAllBytes(file);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                var n = pattern.Matches(text).Count;
                if (n == 0) continue;
                File.WriteAllText(file, pattern.Replace(text, literal), new UTF8Encoding(bom));
                files++;
                hits += n;
            }
            return new[] { files, hits };
        }

        /// <summary>
        /// Every .dll in dir whose CodeView debug record (the .pdb path the compiler writes into an assembly) matches
        /// pathPattern gets that path cut to the .pdb's file name, and the record's size set to match. Nothing else in the
        /// file changes. Returns the file names changed.
        /// </summary>
        public static List<string> StripPdbPaths(string dir, Regex pathPattern)
        {
            var changed = new List<string>();
            if (!Directory.Exists(dir)) return changed;
            foreach (var file in Directory.GetFiles(dir, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var bytes = File.ReadAllBytes(file);
                if (!StripPdbPath(bytes, pathPattern)) continue;
                File.WriteAllBytes(file, bytes);
                changed.Add(Path.GetFileName(file));
            }
            return changed;
        }

        private static bool StripPdbPath(byte[] pe, Regex pathPattern)
        {
            if (pe.Length < 0x40 || pe[0] != 'M' || pe[1] != 'Z') return false;
            var peAt = BitConverter.ToInt32(pe, 0x3C);
            if (peAt <= 0 || peAt + 24 > pe.Length || pe[peAt] != 'P' || pe[peAt + 1] != 'E' || pe[peAt + 2] != 0 || pe[peAt + 3] != 0) return false;
            int sections = BitConverter.ToUInt16(pe, peAt + 6);
            int optionalSize = BitConverter.ToUInt16(pe, peAt + 20);
            var optional = peAt + 24;
            var directories = optional + (BitConverter.ToUInt16(pe, optional) == 0x20B ? 112 : 96);
            if (directories + 7 * 8 > pe.Length) return false;
            var debugRva = BitConverter.ToInt32(pe, directories + 6 * 8);    // data directory 6: debug
            var debugSize = BitConverter.ToInt32(pe, directories + 6 * 8 + 4);
            if (debugRva == 0 || debugSize < 28) return false;
            var debugAt = RvaToOffset(pe, optional + optionalSize, sections, debugRva);
            if (debugAt < 0) return false;
            var changed = false;
            for (var entry = debugAt; entry + 28 <= debugAt + debugSize && entry + 28 <= pe.Length; entry += 28)
            {
                if (BitConverter.ToInt32(pe, entry + 12) != 2) continue;          // IMAGE_DEBUG_TYPE_CODEVIEW
                var size = BitConverter.ToInt32(pe, entry + 16);
                var data = BitConverter.ToInt32(pe, entry + 24);                  // PointerToRawData
                if (size <= 24 || data <= 0 || data + size > pe.Length) continue;
                if (pe[data] != 'R' || pe[data + 1] != 'S' || pe[data + 2] != 'D' || pe[data + 3] != 'S') continue;
                var start = data + 24;                                            // after "RSDS", the GUID and the age
                var end = Array.IndexOf(pe, (byte)0, start, data + size - start);
                if (end < 0) end = data + size;
                var path = Encoding.UTF8.GetString(pe, start, end - start);
                if (!pathPattern.IsMatch(path)) continue;
                var fileName = Encoding.UTF8.GetBytes(Path.GetFileName(path));
                Array.Clear(pe, start, data + size - start);
                Buffer.BlockCopy(fileName, 0, pe, start, fileName.Length);
                BitConverter.GetBytes(24 + fileName.Length + 1).CopyTo(pe, entry + 16);   // SizeOfData
                changed = true;
            }
            return changed;
        }

        private static int RvaToOffset(byte[] pe, int sectionTable, int sections, int rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + i * 40;
                if (s + 40 > pe.Length) return -1;
                var virtualSize = BitConverter.ToInt32(pe, s + 8);
                var virtualAddress = BitConverter.ToInt32(pe, s + 12);
                var rawSize = BitConverter.ToInt32(pe, s + 16);
                var rawAt = BitConverter.ToInt32(pe, s + 20);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize)) return rva - virtualAddress + rawAt;
            }
            return -1;
        }

        /// <summary>
        /// Overwrites every copy of each value stored the way Unity's serialized files store a string (a 4-byte
        /// little-endian length, then the UTF-8 bytes) with as many '0's ('-' kept), so the file's size and every offset
        /// in it stay the same. Returns how many copies were overwritten; the file is written only when there were some.
        /// </summary>
        public static int BlankSerializedStrings(string file, IEnumerable<string> values)
        {
            if (!File.Exists(file)) return 0;
            var bytes = File.ReadAllBytes(file);
            var blanked = 0;
            foreach (var value in (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrEmpty(v)).Distinct())
            {
                var text = Encoding.UTF8.GetBytes(value);
                var stored = BitConverter.GetBytes(text.Length).Concat(text).ToArray();
                for (var at = IndexOf(bytes, stored, 0); at >= 0; at = IndexOf(bytes, stored, at + stored.Length))
                {
                    for (var i = 0; i < text.Length; i++)
                        if (text[i] != (byte)'-') bytes[at + 4 + i] = (byte)'0';
                    blanked++;
                }
            }
            if (blanked > 0) File.WriteAllBytes(file, bytes);
            return blanked;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int from)
        {
            for (var i = from; i <= data.Length - pattern.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        /// <summary>Any of the values, any letter case; null when none has 4 or more characters (too short to look for safely).</summary>
        public static Regex AnyOfPattern(IEnumerable<string> values)
        {
            var list = (values ?? Enumerable.Empty<string>()).Where(v => v != null && v.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(v => v.Length).Select(Regex.Escape).ToList();
            return list.Count == 0 ? null : new Regex(string.Join("|", list), RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// What in folder still names the PC that built it or the Unity account it was built under: the user folder, or an
        /// account value (the project's Unity Cloud organization and project ID), in any file as text or as UTF-16, and
        /// the user's name in the text files. Directories ending in a skip suffix (not shipped) are not read. Returns
        /// "relative path (what)" for each file found.
        /// </summary>
        public static List<string> FindTraces(string folder, Regex userFolder, Regex name, Regex account, params string[] skipDirSuffixes)
        {
            var traces = new List<string>();
            var root = Path.GetFullPath(folder).TrimEnd('\\', '/');
            foreach (var file in FilesToShip(root, skipDirSuffixes ?? new string[0]))
            {
                var bytes = File.ReadAllBytes(file);
                var rel = file.Substring(root.Length).TrimStart('\\', '/');
                bool inFolder = false, inAccount = false;
                foreach (var text in AsText(bytes))
                {
                    inFolder = inFolder || userFolder.IsMatch(text);
                    inAccount = inAccount || (account != null && account.IsMatch(text));
                    if (inFolder && (inAccount || account == null)) break;
                }
                if (inFolder) traces.Add(rel + " (user folder)");
                if (inAccount) traces.Add(rel + " (Unity account)");
                if (name != null && IsText(file) && name.IsMatch(new UTF8Encoding(false).GetString(bytes)))
                    traces.Add(rel + " (name)");
            }
            return traces;
        }

        /// <summary>
        /// A file's bytes read as text three ways, one at a time: one char per byte (ISO-8859-1, so any ASCII shows as
        /// itself), and UTF-16 from the first and from the second byte (C# string literals in an assembly are UTF-16).
        /// </summary>
        private static IEnumerable<string> AsText(byte[] bytes)
        {
            yield return Encoding.GetEncoding(28591).GetString(bytes);
            yield return Encoding.Unicode.GetString(bytes, 0, bytes.Length & ~1);
            if (bytes.Length > 1) yield return Encoding.Unicode.GetString(bytes, 1, (bytes.Length - 1) & ~1);
        }

        // ------------------------------------------------------------------ zip

        /// <summary>zipPath when it is free, else "name (2).zip", "name (3).zip", ...: an earlier zip is never replaced.</summary>
        public static string FreeZipPath(string zipPath)
        {
            if (!File.Exists(zipPath) && !File.Exists(zipPath + ".partial")) return zipPath;
            var dir = Path.GetDirectoryName(zipPath) ?? "";
            var stem = Path.GetFileNameWithoutExtension(zipPath);
            for (var n = 2; ; n++)
            {
                var candidate = Path.Combine(dir, stem + " (" + n + ").zip");
                if (!File.Exists(candidate) && !File.Exists(candidate + ".partial")) return candidate;
            }
        }

        /// <summary>
        /// Zips everything in folder under one top folder in the zip, skipping directories whose name ends with one of
        /// skipDirSuffixes (Unity's *_DoNotShip and *_ButDontShipItWithYourGame folders). Written as zipPath.partial and
        /// renamed when complete, so a zip that exists is always a whole one; zipPath must not exist yet.
        /// </summary>
        public static ZipResult ZipFolder(string folder, string zipPath, string topFolder, params string[] skipDirSuffixes)
        {
            folder = Path.GetFullPath(folder).TrimEnd('\\', '/');
            if (File.Exists(zipPath)) throw new IOException("Not replacing an existing zip: " + zipPath);
            var partial = zipPath + ".partial";
            var result = new ZipResult { ZipPath = zipPath };
            using (var stream = new FileStream(partial, FileMode.CreateNew))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var file in FilesToShip(folder, skipDirSuffixes ?? new string[0]))
                {
                    var rel = file.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/');
                    var entry = zip.CreateEntry(topFolder + "/" + rel, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using (var input = File.OpenRead(file))
                    using (var output = entry.Open())
                    {
                        input.CopyTo(output);
                        result.Bytes += input.Length;
                    }
                    result.Files++;
                }
            }
            File.Move(partial, zipPath);
            result.ZipBytes = new FileInfo(zipPath).Length;
            return result;
        }

        private static IEnumerable<string> FilesToShip(string folder, string[] skipDirSuffixes)
        {
            foreach (var file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                yield return file;
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (skipDirSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (var file in FilesToShip(dir, skipDirSuffixes))
                    yield return file;
            }
        }
    }
}
