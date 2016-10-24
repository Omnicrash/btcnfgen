using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace btcnfgen
{
    class Program
    {
        // App info
        const string version = "v0.1";
        const string name = "btcnfgen";//Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
        static readonly string usage = "Usage: " + name + " "
            + "[-b | -e | -h] [-v] [-y] source [output]\n"
            + "-b\tBuild a binary file from a plaintext file.\n"
            + "-e\tExtract a binary file into a plaintext file.\n"
            + "-v\tForce version (in format #.##).\n"
            + "-c\tAsk for confirmation to overwrite if output file already exists.\n"
            + "-h\tDisplays this info.";

        enum modes
        {
            None,
            Help,
            Build,
            Extract
        }

        [Flags]
        enum section_flags
        {
            VSH = 0x01,
            Game = 0x02,
            Updater = 0x04,
            Pops = 0x08,
            License = 0x10,
            Reserved = 0x20,
            App = 0x40
        }

        [Flags]
        enum load_flags
        {
            NoPercent = 0x01,
            OnePercent = 0x02,
            TwoPercent = 0x04,
            DollarSign = 0x8000
        }

        // Pspbtcnf binary structure
        const int headerSize = 64;
        [StructLayout(LayoutKind.Sequential, Size = headerSize, Pack = 1)]
        struct BtcnfHeader
        {
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x00)]
            public int signature;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x04)]
            public int version;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I4, SizeConst = 2)] //[FieldOffset(0x08)]
            public int[] unknown1;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x10)]
            public int modeStartOffset;
            [MarshalAs(UnmanagedType.I4)]  //[FieldOffset(0x14)]
            public int modeCount;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I4, SizeConst = 2)] //[FieldOffset(0x18)]
            public int[] unknown2;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x20)]
            public int moduleStartOffset;
            [MarshalAs(UnmanagedType.U4)] //[FieldOffset(0x24)]
            public uint moduleCount;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I4, SizeConst = 2)] //[FieldOffset(0x28)]
            public int[] unknown3;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x30)]
            public int moduleNameStartOffset;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x34)]
            public int moduleNameEndOffset;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I4, SizeConst = 2)] //[FieldOffset(0x38)]
            public int[] unknown4;
        }

        const int modeEntrySize = 32;
        [StructLayout(LayoutKind.Sequential, Size = modeEntrySize, Pack = 1)]
        struct ModeEntry
        {
            [MarshalAs(UnmanagedType.U2)] //[FieldOffset(0x00)] 
            public ushort searchMax;
            [MarshalAs(UnmanagedType.U2)] //[FieldOffset(0x02)] 
            public ushort searchStart;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x04)] 
            public int modeFlag;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x08)] 
            public int mode2;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I4, SizeConst = 5)] //[FieldOffset(0x0C)] 
            public int[] reserved;
        }

        const int moduleEntrySize = 32;
        [StructLayout(LayoutKind.Sequential, Size = moduleEntrySize, Pack = 1)]
        struct ModuleEntry
        {
            [MarshalAs(UnmanagedType.U4)] //[FieldOffset(0x00)]
            public uint nameOffset;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x04)]
            public int reserved1;
            [MarshalAs(UnmanagedType.U2)] //[FieldOffset(0x06)]
            public ushort flags;
            [MarshalAs(UnmanagedType.U2)] //[FieldOffset(0x08)]
            public ushort loadmode;
            [MarshalAs(UnmanagedType.I4)] //[FieldOffset(0x0A)]
            public int reserved2;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 0x10)] //[FieldOffset(0x0E)]
            public byte[] hash;
        }

        // Constants and unknown values
        const int sigValid = 0x0F803001;
        const int sigOld = 0x0F803000;
        const int sigEnc = 0x5053507E;

        const int defaultVersion = 0x05050010;

        static readonly int[] unknown1 = new int[2] { 0x6B8B4567, 0x327B23C6 };
        static readonly int[] unknown2 = new int[2] { 0x643C9869, 0x66334873 };
        static readonly int[] unknown3 = new int[2] { 0x74B0DC51, 0x19495CFF };
        static readonly int[] unknown4 = new int[2] { 0x2AE8944A, 0x625558EC };

        static void Main(string[] args)
        {
            Console.WriteLine(name + " " + version);

            modes mode = modes.None;
            int fileVersion = defaultVersion;
            string input = "";
            string output = "";
            bool overwrite = true;

#if !DEBUG
            try
            {
#endif
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i][0] == '-')
                    {
                        switch (args[i][1])
                        {
                            case 'c':
                            case 'C':
                                overwrite = false;
                                break;
                            case 'v':
                            case 'V':
                                fileVersion = SetVersion(args[++i]);
                                break;
                            case 'h':
                            case 'H':
                                if (mode != modes.None) throw new ArgumentException("Multiple modes specified.");
                                mode = modes.Help;
                                break;
                            case 'e':
                            case 'E':
                                if (mode != modes.None) throw new ArgumentException("Multiple modes specified.");
                                mode = modes.Extract;
                                break;
                            case 'b':
                            case 'B':
                                if (mode != modes.None) throw new ArgumentException("Multiple modes specified.");
                                mode = modes.Build;
                                break;
                            default:
                                throw new ArgumentException(String.Format("Argument {0} is not valid.", args[i].Substring(1)));
                        }
                    }
                    else if (input == "") input = args[i];
                    else if (output == "") output = args[i];
                    else throw new ArgumentException(String.Format("Invalid parameter {0}.", args[i]));
                }
                
                if (mode == modes.None)
                {
                    throw new ArgumentException("No mode specified!");
                }
                else if (mode != modes.Help)
                {
                    // Validate files
                    if (input == "") throw new ArgumentException("No input file specified.");
                    if (!File.Exists(input)) throw new FileNotFoundException(String.Format("Input file '{0}' not found.", input));
                    if (output == "") output = input.Substring(0, input.Length - 3) + (mode == modes.Extract ? "txt" : "bin");

                    if (!overwrite && File.Exists(output))
                    {
                        Console.Write(String.Format("File '{0}' already exists, overwrite?", output));
                        ConsoleKeyInfo key = Console.ReadKey();
                        if (key.Key != ConsoleKey.Y) return;
                    }
                }

                switch (mode)
                {
                    case modes.Extract:
                        Extract(input, output);
                        break;
                    case modes.Build:
                        Build(input, output, fileVersion);
                        break;
                    case modes.Help:
                        Console.WriteLine(usage);
                        break;
                }
#if !DEBUG
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                Console.WriteLine(String.Format("Use {0} -h for usage information.", name));

            }
#endif

#if DEBUG
            Console.ReadKey();
#endif
        }

        static void Extract(string input, string output)
        {
            string[] lines;
            using (FileStream bin = new FileStream(input, FileMode.Open, FileAccess.Read))
            {
                // Read header
                BtcnfHeader head;
                try
                {
                    //bin.Seek(0, SeekOrigin.Begin);
                    head = ByteArrayToStructure<BtcnfHeader>(ReadByteArray(bin, 0, headerSize));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("Unable to access or invalid input file.", ex);
                }

                // Check signature
                switch (head.signature)
                {
                    case sigValid:
                        Console.WriteLine(String.Format("v{0} pspbtcnf binary", GetVersion(head.version)));
                        break;
                    case sigEnc:
                        throw new InvalidDataException("Input file is encrypted.");
                    case sigOld:
                        throw new InvalidDataException("Input file is in an older format.");
                    default:
                        throw new InvalidDataException(String.Format("Input file has an unknown signature 0x{0:X8} or is not a valid pspbtcnf file.", head.signature));
                }

                // Mode entries
                //bin.BaseStream.Seek(head.modeStartOffset, 0);
                 
                ModeEntry[] modes = new ModeEntry[head.modeCount];
                for (int i = 0; i < head.modeCount; i++)
                    modes[i] = ByteArrayToStructure<ModeEntry>(ReadByteArray(bin, head.modeStartOffset * i, modeEntrySize));

                // Grab module names
                //bin.BaseStream.Seek(head.moduleNameStartOffset, 0);
                int moduleNameLength = head.moduleNameEndOffset - head.moduleNameStartOffset;
                byte[] moduleNameData = ReadByteArray(bin, head.moduleNameStartOffset, moduleNameLength);
                Dictionary<uint, string> moduleNames = OffsetSplit(ASCIIEncoding.UTF8.GetString(moduleNameData));

                // Fix invalid header
                if (moduleNames.Count != head.moduleCount)
                {
                    Console.WriteLine(String.Format("Header lists {0} modules, file contains {1} modules. Fixed.", head.moduleCount, moduleNames.Count));
                    head.moduleCount = (uint)moduleNames.Count;
                }
                else
                    Console.WriteLine(String.Format("File contains {0} modules.", head.moduleCount));

                Console.Write("Processing module list...");

                // Dump modules
                lines = new string[head.moduleCount];
                for (int i = 0; i < head.moduleCount; i++)
                {
                    //bin.BaseStream.Seek(head.moduleStartOffset + moduleEntrySize * i, SeekOrigin.Begin);
                    
                    ModuleEntry module = ByteArrayToStructure<ModuleEntry>(ReadByteArray(bin, head.moduleStartOffset + moduleEntrySize * i, moduleEntrySize));

                    lines[i] = "";

                    if (((load_flags)module.loadmode & load_flags.DollarSign) == load_flags.DollarSign)
                        lines[i] += "$";
                    if (((load_flags)module.loadmode & load_flags.OnePercent) == load_flags.OnePercent)
                        lines[i] += "%";
                    if (((load_flags)module.loadmode & load_flags.TwoPercent) == load_flags.TwoPercent)
                        lines[i] += "%%";

                    lines[i] += moduleNames[module.nameOffset] + " ";

                    if (((section_flags)module.flags & section_flags.VSH) == section_flags.VSH)
                        lines[i] += "V";
                    if (((section_flags)module.flags & section_flags.Game) == section_flags.Game)
                        lines[i] += "G";
                    if (((section_flags)module.flags & section_flags.Updater) == section_flags.Updater)
                        lines[i] += "U";
                    if (((section_flags)module.flags & section_flags.Pops) == section_flags.Pops)
                        lines[i] += "P";
                    if (((section_flags)module.flags & section_flags.License) == section_flags.License)
                        lines[i] += "L";
                    if (((section_flags)module.flags & section_flags.Reserved) == section_flags.Reserved)
                        lines[i] += "R";
                    if (((section_flags)module.flags & section_flags.App) == section_flags.App)
                        lines[i] += "A";
                }

                bin.Close();
            }

            // Write to output
            using (FileStream txtStream = new FileStream(output, FileMode.Create, FileAccess.Write))
            using (TextWriter txt = new StreamWriter(txtStream))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    txt.WriteLine(lines[i]);
                }
                txt.Flush();
                txt.Close();
            }

            Console.WriteLine("Done!");
            Console.WriteLine(String.Format("Output saved to {0}", output));
        }

        static void Build(string input, string output, int version)
        {
            // Load source file in memory and verify
            List<string[]> valueList = new List<string[]>();
            int lineNumber = 0;
            Regex lineFormat = new Regex(@"^(\$?%{0,2})((?:/[^\\/:\*\?""<>|]+)+)\s(?i)([VGUPLRA]+)$");
            using (FileStream txtStream = new FileStream(input, FileMode.Open, FileAccess.Read))
            using (TextReader txt = new StreamReader(txtStream))
            {
                while (txt.Peek() >= 0)
                {
                    lineNumber++;
                    string line = txt.ReadLine().Trim();
                    if (line != "")
                    {
                        Match validation = lineFormat.Match(line);
                        if (validation.Success)
                        {
                            string[] result = new string[3]
                            {
                                validation.Groups[1].Value,
                                validation.Groups[2].Value,
                                validation.Groups[3].Value
                            };
                            valueList.Add(result);
                        }
                        else
                            throw new InvalidDataException(String.Format("Invalid data in file {0} at line {1}.", input, lineNumber));
                    }
                }
            }
            string[][] values = valueList.ToArray();
            int moduleCount = valueList.Count;
            Console.WriteLine(String.Format("File contains {0} modules.", moduleCount));
            Console.Write(String.Format("Building v{0} pspbtcnf binary...", GetVersion(version)));

            // Build modules & modulelist
            string moduleList = "";
            ModuleEntry[] modules = new ModuleEntry[moduleCount];
            for (int i = 0; i < moduleCount; i++)
            {
                // Modulelist & offset
                modules[i].nameOffset = (uint)moduleList.Length;
                moduleList += values[i][1] + '\0'; 

                // Loadmode flags
                bool dollar = false;
                byte percent = 0;
                for (int a = 0; a < values[i][0].Length; a++)
                {
                    if (values[i][0][a] == '$') dollar = true;
                    else if (values[i][0][a] == '%') percent++;
                }
                switch (percent)
                {
                    case 2:
                        modules[i].loadmode = (ushort)((dollar ? load_flags.DollarSign : 0) | load_flags.TwoPercent);
                        break;
                    case 1:
                        modules[i].loadmode = (ushort)((dollar ? load_flags.DollarSign : 0) | load_flags.OnePercent);
                        break;
                    case 0:
                        modules[i].loadmode = (ushort)((dollar ? load_flags.DollarSign : 0) | load_flags.NoPercent);
                        break;
                }

                // Section flags
                section_flags flags = new section_flags();
                for (int a = 0; a < values[i][2].Length; a++)
                {
                    switch (values[i][2][a])
                    {
                        case 'v':
                        case 'V':
                            flags |= section_flags.VSH;
                            break;
                        case 'g':
                        case 'G':
                            flags |= section_flags.Game;
                            break;
                        case 'u':
                        case 'U':
                            flags |= section_flags.Updater;
                            break;
                        case 'p':
                        case 'P':
                            flags |= section_flags.Pops;
                            break;
                        case 'l':
                        case 'L':
                            flags |= section_flags.License;
                            break;
                        case 'r':
                        case 'R':
                            flags |= section_flags.Reserved;
                            break;
                        case 'a':
                        case 'A':
                            flags |= section_flags.App;
                            break;
                    }
                }
                modules[i].flags = (ushort)flags;

                // Fill in unknown/unused info
                modules[i].hash = new byte[0x10];
                modules[i].reserved1 = 0;
                modules[i].reserved2 = 0;
            }

            // Build mode entries
            ModeEntry[] modes = new ModeEntry[5];
            modes[0].searchMax = (ushort)moduleCount;
            modes[0].modeFlag = (int)section_flags.VSH;
            modes[0].mode2 = 2;
            modes[0].reserved = new int[5];
            modes[0].searchStart = 0;

            modes[1].searchMax = (ushort)moduleCount;
            modes[1].modeFlag = (int)section_flags.Game;
            modes[1].mode2 = 1;
            modes[1].reserved = new int[5];
            modes[1].searchStart = 0;

            modes[2].searchMax = (ushort)moduleCount;
            modes[2].modeFlag = (int)section_flags.Updater;
            modes[2].mode2 = 3;
            modes[2].reserved = new int[5];
            modes[2].searchStart = 0;

            modes[3].searchMax = (ushort)moduleCount;
            modes[3].modeFlag = (int)section_flags.Pops;
            modes[3].mode2 = 4;
            modes[3].reserved = new int[5];
            modes[3].searchStart = 0;

            modes[4].searchMax = (ushort)moduleCount;
            modes[4].modeFlag = (int)section_flags.App;
            modes[4].mode2 = 7;
            modes[4].reserved = new int[5];
            modes[4].searchStart = 0;

            int modeCount = modes.Length;

            // Build header
            BtcnfHeader head = new BtcnfHeader();
            head.modeStartOffset = headerSize;
            head.modeCount = modeCount;
            head.moduleStartOffset = head.modeStartOffset + modeEntrySize * modeCount;
            head.moduleCount = (uint)moduleCount;
            head.moduleNameStartOffset = head.moduleStartOffset + moduleEntrySize * moduleCount;
            head.moduleNameEndOffset = head.moduleNameStartOffset + moduleList.Length;
            head.unknown1 = unknown1;
            head.unknown2 = unknown2;
            head.unknown3 = unknown3;
            head.unknown4 = unknown4;
            head.version = version;
            head.signature = sigValid;
            
            // Write output
            using (FileStream bin = new FileStream(output, FileMode.Create, FileAccess.Write))
            {
                byte[] headerBytes = StructureToByteArray<BtcnfHeader>(head, headerSize);
                bin.Write(headerBytes, 0, headerBytes.Length);

                for (int i = 0; i < modes.Length; i++)
                {
                    byte[] modeBytes = StructureToByteArray<ModeEntry>(modes[i], modeEntrySize);
                    bin.Seek(head.modeStartOffset + i * modeEntrySize, SeekOrigin.Begin);
                    bin.Write(modeBytes, 0, modeBytes.Length);
                }

                for (int i = 0; i < moduleCount; i++)
                {
                    byte[] moduleBytes = StructureToByteArray<ModuleEntry>(modules[i], moduleEntrySize);
                    bin.Seek(head.moduleStartOffset + i * moduleEntrySize, SeekOrigin.Begin);
                    bin.Write(moduleBytes, 0, moduleBytes.Length);
                }

                byte[] moduleListBytes = ASCIIEncoding.UTF8.GetBytes(moduleList);
                bin.Seek(head.moduleNameStartOffset, SeekOrigin.Begin);
                bin.Write(moduleListBytes, 0, moduleListBytes.Length);

                bin.Flush();
                bin.Close();
            }

            Console.WriteLine("Done!");
            Console.WriteLine(String.Format("Output saved to {0}", output));
        }

        static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();
            return result;
        }

        static byte[] StructureToByteArray<T>(T structure, int size) where T : struct
        {
            byte[] result = new byte[size];
            GCHandle handle = GCHandle.Alloc(result, GCHandleType.Pinned);
            Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), true);
            handle.Free();
            return result;
        }

        static string GetVersion(int version)
        {
            byte[] verByte = BitConverter.GetBytes(version);
            return verByte[3].ToString() + "." + verByte[2].ToString() + verByte[1].ToString();
            // Byte 0 is always 0x10... Release version indicator?
        }

        static int SetVersion(string version)
        {
            // Validate string
            Regex versionCheck = new Regex(@"^\d+\.\d{2}$");
            if (!versionCheck.IsMatch(version)) throw new InvalidDataException(String.Format("The specified version '{0}' is not in the format '#.##'.", version));

            byte[] verByte = new byte[4];
            verByte[0] = 0x10;
            verByte[1] = byte.Parse(version.Substring(version.IndexOf('.') + 2, 1));
            verByte[2] = byte.Parse(version.Substring(version.IndexOf('.') + 1, 1));
            verByte[3] = byte.Parse(version.Substring(0, version.IndexOf('.')));
            return BitConverter.ToInt32(verByte, 0);
        }

        private static Dictionary<uint, string> OffsetSplit(string nameList)
        {
            //int nameCount = nameList.Split('\0').Length - 1;
            Dictionary<uint, string> result = new Dictionary<uint, string>();
            //ModuleNameEntry[] result = new ModuleNameEntry[nameCount];
            uint offset = 0; string item = ""; int itemCount = 0;
            for (int i = 0; i < nameList.Length; i++)
            {
                if (nameList[i] != '\0')
                    item += nameList[i];
                else
                {
                    //result[itemCount].name = item;
                    //result[itemCount].offset = offset;
                    result.Add(offset, item);
                    itemCount++;
                    offset = (uint)i + 1;
                    item = "";
                }
            }
            return result;
        }

        /// <summary>
        /// Reads a byte array from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="offset">The offset from the beginning of the stream to start reading.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <returns></returns>
        public static byte[] ReadByteArray(Stream stream, int offset, int length)
        {
            byte[] result = new byte[length];
            stream.Seek(offset, SeekOrigin.Begin);
            int bytesLeft = length;
            int curOffset = 0;
            while (bytesLeft > 0)
            {
                int read = stream.Read(result, curOffset, bytesLeft);
                if (read <= 0) throw new EndOfStreamException(String.Format("Reached end of stream with {0} bytes left to read.", bytesLeft));
                bytesLeft -= read;
                curOffset += read;
            }
            return result;
        }
    }
}
