using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ArcSysAPI.Common.Enums;
using ArcSysAPI.Models;
using ArcSysAPI.Utils;
using GeoArcSysCryptTool.Models;
using GeoArcSysCryptTool.Utils;

namespace GeoArcSysCryptTool
{
    internal class Program
    {
        [Flags]
        public enum Games
        {
            BBCT = 0x1,
            BBCSEX = 0x2,
            BBCPEX = 0x4,
            BBTAG = 0x8
        }

        [Flags]
        public enum Modes
        {
            Encrypt = 0x1,
            Decrypt = 0x2,
            MD5Encrypt = 0x4,
            MD5Decrypt = 0x8,
            Deflate = 0x10,
            Inflate = 0x20,
            SwitchDeflate = 0x40,
            SwitchInflate = 0x80,
            Auto = 0x40000000
        }

        [Flags]
        public enum Options
        {
            Mode = 0x1,
            Game = 0x2,
            Paths = 0x4,
            Output = 0x10000000,
            Replace = 0x20000000,
            Continue = 0x40000000
        }

        public static ConsoleOption[] ConsoleOptions =
        {
            new ConsoleOption
            {
                Name = "Mode",
                ShortOp = "-m",
                LongOp = "--mode",
                Description =
                    "Specifies to {Encrypt|Decrypt|MD5Encrypt|MD5Decrypt|Deflate|Inflate\n\t\t\t|SwitchDeflate|SwitchInflate|Auto} the file. Without this option, \n\t\t\tit'll automatically decide.",
                HasArg = true,
                Flag = Options.Mode
            },
            new ConsoleOption
            {
                Name = "Game",
                ShortOp = "-g",
                LongOp = "--game",
                Description =
                    "Specifies the targeted game {BBCT|BBCSEX|BBCPEX|BBTAG} to assist in \n\t\t\tthe automatic mode.",
                HasArg = true,
                Flag = Options.Game
            },
            new ConsoleOption
            {
                Name = "Paths",
                ShortOp = "-p",
                LongOp = "--paths",
                Description =
                    "Provides a path to a file containing a list of file paths which \n\t\t\twill be used when dealing with the MD5Decrypt mode. Otherwise it \n\t\t\twill default to \"paths.txt\" in the same directory as executable.",
                HasArg = true,
                Flag = Options.Paths
            },
            new ConsoleOption
            {
                Name = "Output",
                ShortOp = "-o",
                LongOp = "--output",
                Description = "Specifies the output directory for the output files.",
                HasArg = true,
                Flag = Options.Output
            },
            new ConsoleOption
            {
                Name = "Replace",
                ShortOp = "-r",
                LongOp = "--replace",
                Description = "Don't create a backup and replace same named files in output directory.",
                Flag = Options.Replace
            },
            new ConsoleOption
            {
                Name = "Continue",
                ShortOp = "-c",
                LongOp = "--continue",
                Description = "Don't pause the application when finished.",
                Flag = Options.Continue
            }
        };

        public static string assemblyPath = string.Empty;
        public static string assemblyDir = string.Empty;

        public static string initFilePath = string.Empty;
        public static string currentFile = string.Empty;

        public static Options options;
        public static Modes modes;
        public static Games games;

        public static string pathsFile = string.Empty;
        public static FilePaths[] pathsArray;
        public static string outputPath = string.Empty;

        public static string[] BBTAGObfuscatedFiles =
            {string.Empty, ".pac", ".pacgz", ".hip", ".abc", ".txt", ".pat", ".ha6", ".fod"};

        [STAThread]
        private static void Main(string[] args)
        {
            var codeBase = Assembly.GetExecutingAssembly().CodeBase;
            var uri = new UriBuilder(codeBase);
            assemblyPath = Path.GetFullPath(Uri.UnescapeDataString(uri.Path));
            assemblyDir = Path.GetDirectoryName(assemblyPath);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nArcSystemWorks Crypt Tool\nprogrammed by: Geo\n\n");
            Console.ForegroundColor = ConsoleColor.White;

            try
            {
                if (args.Length == 0 ||
                    args.Length > 0 && (args[0] == "-h" || args[0] == "--help"))
                {
                    ShowUsage();
                    Pause();
                    return;
                }

                var firstArgNullWhitespace = string.IsNullOrWhiteSpace(args[0]);
                if (firstArgNullWhitespace || args[0].First() == '-')
                {
                    var inputPath = Dialogs.OpenFileDialog("Select input file...");
                    if (string.IsNullOrWhiteSpace(inputPath))
                    {
                        inputPath = Dialogs.OpenFolderDialog("Select input folder...");
                        if (string.IsNullOrWhiteSpace(inputPath))
                        {
                            ShowUsage();
                            Pause();
                            return;
                        }
                    }

                    if (firstArgNullWhitespace)
                    {
                        args[0] = inputPath;
                    }
                    else
                    {
                        var argsList = new List<string>(args);
                        argsList.Insert(0, inputPath);
                        args = argsList.ToArray();
                    }
                }

                initFilePath = Path.GetFullPath(args[0].Replace("\"", "\\"));

                ProcessOptions(args);

                if (!File.Exists(initFilePath) && !Directory.Exists(initFilePath) ||
                    string.IsNullOrWhiteSpace(initFilePath) || initFilePath.First() == '-')
                {
                    Console.WriteLine("The given file/folder does not exist.\n");
                    Pause();
                    return;
                }

                ProcessGamesAndModes();

                if (File.GetAttributes(initFilePath).HasFlag(FileAttributes.Directory))
                {
                    var files = DirSearch(initFilePath);
                    var origModes = modes;
                    foreach (var file in files)
                    {
                        ProcessFile(file);
                        modes = origModes;
                    }
                }
                else
                {
                    ProcessFile(initFilePath);
                }

                Console.WriteLine("Complete.");
                Pause();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                if (!string.IsNullOrWhiteSpace(currentFile))
                    Console.WriteLine($"Current File: {currentFile}");
                Console.WriteLine(ex);
                Console.WriteLine("Something went wrong!");
                Console.ForegroundColor = ConsoleColor.White;
                Pause();
            }
        }

        public static void ProcessOptions(string[] args)
        {
            var newArgsList = new List<string>();

            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.First() != '-')
                    continue;

                newArgsList.Add(arg);

                foreach (var co in ConsoleOptions)
                    if (arg == co.ShortOp || arg == co.LongOp)
                    {
                        options |= (Options) co.Flag;
                        if (co.HasArg)
                        {
                            var subArgsList = new List<string>();
                            var lastArg = string.Empty;
                            for (var j = i; j < args.Length - 1; j++)
                            {
                                var subArg = args[j + 1];

                                if (subArg.First() == '-')
                                    break;

                                if (string.IsNullOrWhiteSpace(lastArg) || subArg.ToLower() != lastArg.ToLower())
                                    subArgsList.Add(subArg);
                                i++;
                            }

                            co.SpecialObject = subArgsList.ToArray();
                        }
                    }
            }

            foreach (var co in ConsoleOptions)
            {
                if (co.Flag == null)
                    continue;

                if (co.HasArg)
                {
                    var subArgs = (string[]) co.SpecialObject;
                    if ((Options) co.Flag == Options.Mode &&
                        options.HasFlag(Options.Mode))
                    {
                        if (subArgs.Length > 0)
                            foreach (var arg in subArgs)
                            {
                                Modes mode;
                                if (Enum.TryParse(arg, true, out mode)) modes |= mode;
                            }
                        else
                            modes |= Modes.Auto;
                    }

                    if ((Options) co.Flag == Options.Game &&
                        options.HasFlag(Options.Game))
                    {
                        for (var i = 0; i < subArgs.Length; i++)
                            subArgs[i] = subArgs[i].ToUpper();
                        foreach (var arg in subArgs)
                        {
                            Games game;
                            if (Enum.TryParse(arg, true, out game))
                            {
                                if (subArgs.Length > 1)
                                    WarningMessage(
                                        $"Too many arguments for game. Defaulting to \"{arg}\"...");
                                games |= game;
                                break;
                            }
                        }

                        if (games == 0)
                        {
                            if (subArgs.Length > 1)
                                WarningMessage(
                                    "None of the given games are compatible. Ignoring...");
                            else if (subArgs.Length == 1)
                                WarningMessage(
                                    "Given game was not compatible. Ignoring...");
                            else if (subArgs.Length == 0)
                                WarningMessage(
                                    "No game was given. Ignoring...");
                        }
                    }

                    if ((Options) co.Flag == Options.Paths &&
                        options.HasFlag(Options.Paths))
                    {
                        var defaultPath = Path.Combine(assemblyDir, "paths.txt");
                        if (subArgs.Length == 0)
                        {
                            subArgs = new string[1];
                            if (File.Exists(defaultPath))
                            {
                                InfoMessage(
                                    "Using default paths text file...");

                                subArgs[0] = defaultPath;
                            }
                            else
                            {
                                subArgs[0] = Dialogs.OpenFileDialog("Select paths text file...");
                            }
                        }

                        foreach (var arg in subArgs)
                            if (File.Exists(arg))
                            {
                                if (subArgs.Length > 1)
                                    WarningMessage(
                                        $"Too many arguments for paths text file. Defaulting to \"{arg}\"...");
                                pathsFile = arg;
                                UpdatePaths();
                                break;
                            }

                        if (string.IsNullOrWhiteSpace(pathsFile))
                        {
                            if (subArgs.Length > 1)
                                WarningMessage(
                                    "None of the given paths text files exist. Ignoring...");
                            else if (subArgs.Length == 1)
                                WarningMessage(
                                    "Given paths text file does not exist. Ignoring...");

                            if (File.Exists(defaultPath))
                            {
                                InfoMessage(
                                    "Using default paths text file...");

                                pathsFile = defaultPath;
                                UpdatePaths();
                            }
                            else if (subArgs.Length == 0)
                            {
                                WarningMessage(
                                    "No paths text file was given. Ignoring...");
                            }
                        }
                    }

                    if ((Options) co.Flag == Options.Output &&
                        options.HasFlag(Options.Output))
                    {
                        if (subArgs.Length == 0)
                        {
                            subArgs = new string[1];
                            subArgs[0] = Dialogs.OpenFolderDialog("Select output folder...");
                        }

                        foreach (var arg in subArgs)
                        {
                            var subArg = Path.GetFullPath(arg.Replace("\"", "\\"));
                            if (subArgs.Length > 1)
                                WarningMessage(
                                    $"Too many arguments for output path. Defaulting to \"{subArg}\"...");
                            outputPath = Path.GetFullPath(subArg);
                            break;
                        }

                        var defaultPath = assemblyDir;

                        if (string.IsNullOrWhiteSpace(outputPath))
                        {
                            if (subArgs.Length > 1)
                                WarningMessage(
                                    "None of the given output paths exist or could be created. Ignoring...");
                            else if (subArgs.Length == 1)
                                WarningMessage(
                                    "Given output path does not exist. Ignoring...");
                            else if (subArgs.Length == 0)
                                WarningMessage(
                                    "No output path was given. Ignoring...");

                            if (Directory.Exists(defaultPath))
                            {
                                InfoMessage(
                                    "Using default output path...");

                                outputPath = defaultPath;
                            }
                        }
                        else
                        {
                            if (!Directory.Exists(outputPath))
                            {
                                WarningMessage(
                                    "Given output path does not exist. Ignoring...");
                                if (Directory.Exists(defaultPath))
                                {
                                    InfoMessage(
                                        "Using default output path...");

                                    outputPath = defaultPath;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void ProcessGamesAndModes()
        {
            if (string.IsNullOrWhiteSpace(outputPath)) outputPath = assemblyDir;

            /*if (string.IsNullOrWhiteSpace(pathsFile))
            {
                var path = Path.Combine(assemblyDir, "paths.txt");
                if (File.Exists(path))
                    pathsFile = path;
            }*/
            if (modes == 0)
                modes = Modes.Auto;

            if (modes.HasFlag(Modes.Auto) && modes != Modes.Auto)
            {
                InfoMessage("Auto mode supersedes all other modes. Continuing to use Auto mode...");
                modes = Modes.Auto;
            }
            else if (modes.HasFlag(Modes.Encrypt) && modes.HasFlag(Modes.Decrypt) ||
                     modes.HasFlag(Modes.MD5Encrypt) && modes.HasFlag(Modes.MD5Decrypt) ||
                     modes.HasFlag(Modes.Deflate) && modes.HasFlag(Modes.Inflate) ||
                     modes.HasFlag(Modes.SwitchDeflate) && modes.HasFlag(Modes.SwitchInflate))
            {
                WarningMessage("Can't use opposing modes. Defaulting to Auto mode...");
                modes = Modes.Auto;
            }
            else if (!modes.HasFlag(Modes.Auto))
            {
                if (games.HasFlag(Games.BBCT) || games.HasFlag(Games.BBCSEX) || games.HasFlag(Games.BBCPEX))
                {
                    if (modes.HasFlag(Modes.MD5Encrypt) || modes.HasFlag(Modes.MD5Decrypt))
                    {
                        WarningMessage("Specified game does not use MD5 cryptography. Defaulting to Auto mode...");
                        modes = Modes.Auto;
                    }
                    else if (modes.HasFlag(Modes.SwitchDeflate) || modes.HasFlag(Modes.SwitchInflate))
                    {
                        WarningMessage("Specified game does not use Switch compression. Defaulting to Auto mode...");
                        modes = Modes.Auto;
                    }
                }
                else if (games.HasFlag(Games.BBTAG))
                {
                    if (modes.HasFlag(Modes.Encrypt) ||
                        modes.HasFlag(Modes.Decrypt) ||
                        modes.HasFlag(Modes.Inflate) ||
                        modes.HasFlag(Modes.Deflate))
                    {
                        WarningMessage("Specified game only uses MD5 cryptography. Defaulting to Auto mode...");
                        modes = Modes.Auto;
                    }
                    else if ((modes.HasFlag(Modes.MD5Decrypt) ||
                              modes.HasFlag(Modes.MD5Encrypt)) &&
                             (modes.HasFlag(Modes.SwitchDeflate) ||
                              modes.HasFlag(Modes.SwitchInflate)))
                    {
                        WarningMessage(
                            "Specified game does not use both MD5 Encryption and Switch Compression combined. Defaulting to Auto mode...");
                        modes = Modes.Auto;
                    }
                }

                if (games.HasFlag(Games.BBCT) && (modes.HasFlag(Modes.Inflate) ||
                                                  modes.HasFlag(Modes.Deflate)))
                {
                    WarningMessage("Specified game does not use compression. Defaulting to Auto mode...");
                    modes = Modes.Auto;
                }
            }
        }

        public static void ProcessFile(string file)
        {
            currentFile = file;
            var fileName = Path.GetFileName(file);
            Console.WriteLine($"Processing {fileName}...");

            if (!File.Exists(file))
            {
                WarningMessage("File does not exist. Skipping...");
                return;
            }

            var ext = Path.GetExtension(file).ToLower();
            if ((games.HasFlag(Games.BBCT) ||
                 games.HasFlag(Games.BBCSEX) ||
                 games.HasFlag(Games.BBCPEX)) && ext != ".pac")
            {
                InfoMessage("Specified game only obfuscates .pac files. Skipping...");
                return;
            }

            if (games.HasFlag(Games.BBTAG) && !BBTAGObfuscatedFiles.Contains(ext))
            {
                InfoMessage($"Specified game does not obfuscate {ext} files. Skipping...");
                return;
            }

            if (ext == ".pacgz" && !modes.HasFlag(Modes.SwitchDeflate) && !modes.HasFlag(Modes.SwitchInflate) &&
                !modes.HasFlag(Modes.Auto))
            {
                InfoMessage($"Specified game and mode does not obfuscate {ext} files. Skipping...");
                return;
            }

            if (string.IsNullOrWhiteSpace(ext) &&
                (modes.HasFlag(Modes.SwitchDeflate) || modes.HasFlag(Modes.SwitchInflate)))
            {
                InfoMessage("Specified game and mode does not obfuscate empty exetension files. Skipping...");
                return;
            }

            byte[] fileBytes = null;
            var fileDirectory = outputPath;

            var magicBytes = new byte[4];
            using (var fs =
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Read(magicBytes, 0, 4);
                fs.Close();
            }

            var isPACMB = magicBytes.SequenceEqual(new byte[] {0x46, 0x50, 0x41, 0x43});
            var isHIPMB = magicBytes.SequenceEqual(new byte[] {0x48, 0x49, 0x50, 0x00});
            var isHPLMB = magicBytes.SequenceEqual(new byte[] {0x48, 0x50, 0x41, 0x4C});

            var fileIsKnown = isPACMB || isHIPMB || isHPLMB;

            if (modes == Modes.Auto || (games == Games.BBCSEX || games == Games.BBCPEX) && modes == 0)
                if (magicBytes.SequenceEqual(new byte[] {0x44, 0x46, 0x41, 0x53}))
                    modes = Modes.Inflate;

            if (modes == Modes.Auto || games == Games.BBTAG && modes == 0)
            {
                if (magicBytes.Take(3).SequenceEqual(new byte[] {0x1F, 0x8B, 0x08}))
                    modes = Modes.SwitchInflate;
                else if (MD5Tools.IsMD5(fileName)) modes = Modes.MD5Decrypt;
                else if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32))) modes = Modes.MD5Encrypt;
            }

            if (modes == Modes.Auto || games != 0 && modes == 0)
                switch (games)
                {
                    case Games.BBCT:
                        modes = fileIsKnown ? Modes.Encrypt : Modes.Decrypt;
                        break;
                    case Games.BBTAG:
                        modes = fileIsKnown ? Modes.MD5Encrypt : Modes.MD5Decrypt;
                        break;
                    case Games.BBCSEX:
                    case Games.BBCPEX:
                    case 0:
                        modes = fileIsKnown ? Modes.Deflate | Modes.Encrypt : Modes.Inflate | Modes.Decrypt;
                        break;
                }

            if (modes == Modes.Auto || fileIsKnown && (modes.HasFlag(Modes.Decrypt) ||
                                                       modes.HasFlag(Modes.MD5Decrypt) ||
                                                       modes.HasFlag(Modes.Inflate) ||
                                                       modes.HasFlag(Modes.SwitchInflate)))
            {
                var pacFile = new PACFileInfo(file);
                if (pacFile.IsValidPAC)
                {
                    fileBytes = pacFile.GetBytes();
                }
                else
                {
                    var hipFile = new HIPFileInfo(file);
                    if (hipFile.IsValidHIP)
                    {
                        fileBytes = pacFile.GetBytes();
                    }
                    else
                    {
                        var hplFile = new HPLFileInfo(file);
                        if (hplFile.IsValidHPL) fileBytes = pacFile.GetBytes();
                    }
                }
            }

            if (fileBytes == null)
            {
                var changed = false;
                var memStream = new MemoryStream();
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.CopyTo(memStream);
                }

                if (modes.HasFlag(Modes.Deflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Deflating {fileName}...");
                    var ms = BBObfuscatorTools.DFASFPACDeflateStream(memStream);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.Decrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Decrypting {fileName}...");
                    var ms = BBObfuscatorTools.FPACCryptStream(memStream, file, CryptMode.Decrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }

                if (modes.HasFlag(Modes.Encrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Encrypting {fileName}...");
                    var ms = BBObfuscatorTools.FPACCryptStream(memStream, file, CryptMode.Encrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.Inflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Inflating {fileName}...");
                    var ms = BBObfuscatorTools.DFASFPACInflateStream(memStream);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    changed = true;
                }
                else if (modes.HasFlag(Modes.MD5Encrypt))
                {
                    memStream.Position = 0;
                    if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32)) ||
                        file.LastIndexOf("data") >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"MD5 Encrypting {fileName}...");
                        var ms = BBTAGMD5CryptTools.BBTAGMD5CryptStream(memStream, file, CryptMode.Encrypt);
                        memStream.Close();
                        memStream.Dispose();
                        memStream = ms;
                        if (fileName.Length > 32 && MD5Tools.IsMD5(fileName.Substring(0, 32)))
                        {
                            fileName = fileName.Substring(0, 32);
                        }
                        else if (!MD5Tools.IsMD5(fileName))
                        {
                            var lastIndex = file.LastIndexOf("data");
                            var datapath = file.Substring(lastIndex, file.Length - lastIndex);
                            fileName = MD5Tools.CreateMD5(datapath.Replace("\\", "/"));
                            file = fileName;
                        }

                        changed = true;
                    }
                    else
                    {
                        WarningMessage(
                            "File's name and/or directory does not follow the rules for MD5 Encryption. Ignoring...");
                    }
                }
                else if (modes.HasFlag(Modes.MD5Decrypt))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"MD5 Decrypting {fileName}...");
                    var ms = BBTAGMD5CryptTools.BBTAGMD5CryptStream(memStream, file, CryptMode.Decrypt);
                    memStream.Close();
                    memStream.Dispose();
                    memStream = ms;
                    if (MD5Tools.IsMD5(fileName))
                    {
                        if (!string.IsNullOrWhiteSpace(pathsFile))
                        {
                            var length = pathsArray.Length;
                            for (var i = 0; i < length; i++)
                                if (pathsArray[i].filepathMD5 == fileName)
                                {
                                    var filepath = pathsArray[i].filepath;
                                    fileName = Path.GetFileName(filepath);
                                    fileDirectory = Path.Combine(outputPath, Path.GetDirectoryName(filepath));
                                }
                        }

                        if (MD5Tools.IsMD5(fileName)) fileName = fileName + "_" + StringToByteArray(fileName)[7] % 43;
                    }

                    changed = true;
                }
                else if (modes.HasFlag(Modes.SwitchDeflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Switch Deflating {fileName}...");
                    var output = new MemoryStream();
                    var data = memStream.ToArray();
                    using (Stream input = new GZipStream(output,
                        CompressionLevel.Optimal, true))
                    {
                        input.Write(data, 0, data.Length);
                        input.Close();
                    }

                    memStream.Close();
                    memStream.Dispose();
                    memStream = output;
                    changed = true;
                    if (ext == ".pac")
                        fileName = Path.GetFileNameWithoutExtension(fileName) + ".pacgz";
                }
                else if (modes.HasFlag(Modes.SwitchInflate))
                {
                    memStream.Position = 0;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Switch Inflating {fileName}...");
                    using (Stream input = new GZipStream(new MemoryStream(memStream.GetBuffer()),
                        CompressionMode.Decompress, true))
                    {
                        using (var output = new MemoryStream())
                        {
                            input.CopyTo(output);
                            input.Close();
                            memStream.Close();
                            memStream.Dispose();
                            memStream = new MemoryStream(output.ToArray());
                        }
                    }

                    changed = true;

                    if (ext == ".pacgz")
                        fileName = Path.GetFileNameWithoutExtension(fileName) + ".pac";
                }

                Console.ForegroundColor = ConsoleColor.White;
                if (changed)
                    fileBytes = memStream.ToArray();
                memStream.Close();
                memStream.Dispose();
            }

            if (fileBytes == null)
            {
                var automaticString = modes == Modes.Auto || games != 0 && modes == 0 ? " automatically" : string.Empty;
                WarningMessage($"Could not{automaticString} process {fileName}.");
                return;
            }

            var directory = initFilePath == file || file == fileName
                ? fileDirectory
                : Path.Combine(fileDirectory, Path.GetDirectoryName(file).Replace(initFilePath, string.Empty));
            Directory.CreateDirectory(directory);
            var filePath = Path.GetFullPath(Path.Combine(directory, fileName));

            if (File.Exists(filePath) && !options.HasFlag(Options.Replace))
            {
                var backupPath = filePath + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(filePath, backupPath);
            }

            File.WriteAllBytes(filePath, fileBytes);

            Console.Write("Finished processing ");
            var resultFileConsoleColor = ConsoleColor.White;
            if ((modes.HasFlag(Modes.Encrypt) || modes.HasFlag(Modes.MD5Encrypt)) && modes.HasFlag(Modes.Deflate))
                resultFileConsoleColor = ConsoleColor.Magenta;
            else if (modes.HasFlag(Modes.Deflate))
                resultFileConsoleColor = ConsoleColor.Cyan;
            else if (modes.HasFlag(Modes.Encrypt) || modes.HasFlag(Modes.MD5Encrypt))
                resultFileConsoleColor = ConsoleColor.Green;
            Console.ForegroundColor = resultFileConsoleColor;
            Console.Write($"{fileName}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(".");
        }

        private static void ShowUsage()
        {
            var shortOpMaxLength =
                ConsoleOptions.Select(co => co.ShortOp).OrderByDescending(s => s.Length).First().Length;
            var longOpMaxLength =
                ConsoleOptions.Select(co => co.LongOp).OrderByDescending(s => s.Length).First().Length;

            Console.WriteLine(
                $"Usage: {Path.GetFileName(assemblyPath)} <file/folder path> [options...]");

            Console.WriteLine("Options:");
            foreach (var co in ConsoleOptions)
                Console.WriteLine(
                    $"{co.ShortOp.PadRight(shortOpMaxLength)}\t{co.LongOp.PadRight(longOpMaxLength)}\t{co.Description}");
        }

        private static void UpdatePaths()
        {
            var pathList = new List<FilePaths>();
            using (TextReader reader = File.OpenText(pathsFile))
            {
                var pattern = new Regex("[/\"]|[/]{2}");
                while (reader.Peek() >= 0)
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        line = pattern.Replace(line, "/").ToLower();
                        try
                        {
                            Path.GetFullPath(line);
                            var lineMD5 = MD5Tools.CreateMD5(line);
                            line = line.Replace("/", "\\");
                            pathList.Add(new FilePaths(line, lineMD5));
                        }
                        catch
                        {
                        }
                    }
                }
            }

            pathsArray = pathList.ToArray();
        }

        private static void Pause(bool force = false)
        {
            if (options.HasFlag(Options.Continue) && !force)
                return;

            Console.WriteLine("\rPress Any Key to exit...");
            Console.ReadKey();
        }

        private static void InfoMessage(string message)
        {
            ConsoleMessage(message, ConsoleColor.Blue, "INFO");
        }

        private static void WarningMessage(string message)
        {
            ConsoleMessage(message, ConsoleColor.DarkYellow, "WARNING");
        }

        private static void ConsoleMessage(string message, ConsoleColor color, string messageType)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{messageType}] {message}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static string[] DirSearch(string sDir)
        {
            var stringList = new List<string>();
            foreach (var f in Directory.GetFiles(sDir)) stringList.Add(f);
            foreach (var d in Directory.GetDirectories(sDir)) stringList.AddRange(DirSearch(d));

            return stringList.ToArray();
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public struct FilePaths
        {
            public string filepath, filepathMD5;

            public FilePaths(string p1, string p2)
            {
                filepath = p1;
                filepathMD5 = p2;
            }
        }
    }
}