using System.Diagnostics;

namespace InterfaceBuilder;

internal static class InterfaceCompiler
{
    private static readonly Dictionary<string, string?> Config = new();

    private static string? _interfaceDir;
    private static string? _clientDir;
    private static string[]? _deleteFiles;

    private static void Main()
    {
        Console.Title = "Interface Builder Launcher By Mk";

        var lines = File.ReadAllLines("config.properties");
        foreach (var line in lines)
        {
            var split = line.Split('=');
            var key = split[0].Trim();
            var value = split[1].Trim();
            Config.Add(key, value);
        }

        _interfaceDir = Config["InterfaceDir"];
        _clientDir = Config["ClientDir"];
        
        if (Config.TryGetValue("DeleteFiles", out var deleteFilesConfig) && !string.IsNullOrEmpty(deleteFilesConfig))
        {
            _deleteFiles = deleteFilesConfig.Split(',').Select(f => f.Trim()).ToArray();
        }

        Console.WriteLine(" =================================");
        Console.WriteLine(" Interface Builder Launcher By Mk");
        Console.WriteLine(" =================================");

        try
        {
            CloseL2Process();
            CompileInterface();
            CopyCompiledFile();
            StartL2();
            Console.WriteLine("================================");
            Console.WriteLine("Processo concluído com sucesso!");
            Console.WriteLine("================================");
        }
        catch (Exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.ResetColor();
            Console.WriteLine(" Pressione qualquer tecla para sair...");
            Console.ReadKey();
        }
    }

    private static void CloseL2Process()
    {
        Console.WriteLine(" Verificando processo L2.exe...");
        var processes = Process.GetProcessesByName("l2");
        if (processes.Length <= 0) return;
        foreach (var process in processes)
        {
            try
            {
                var processPath = process.MainModule?.FileName;

                if (string.IsNullOrEmpty(processPath)) continue;
                var processFolder = Path.GetDirectoryName(processPath);

                if (!string.IsNullOrEmpty(processFolder))
                {
                }

                if (string.IsNullOrEmpty(processFolder) || string.IsNullOrEmpty(_clientDir)) continue;
                var normalizedProcessFolder = Path.GetFullPath(processFolder).TrimEnd('\\', '/');
                var normalizedClientDir = Path.GetFullPath(_clientDir).TrimEnd('\\', '/');

                if (!string.Equals(normalizedProcessFolder, normalizedClientDir, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill();
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erro ao fechar PID {process.Id}: {ex.Message}");
            }
        }
    }

    private static void CompileInterface()
    {
        Console.WriteLine(" Compilando interface...");
        if (_interfaceDir == null || _clientDir == null)
        {
            throw new InvalidOperationException("InterfaceDir ou ClientDir não configurados");
        }
        DeleteCompiledFiles();
        var uccPath = Path.Combine(_interfaceDir, "UCC.exe");
        if (!File.Exists(uccPath))
        {
            throw new FileNotFoundException($"UCC.exe não encontrado: {uccPath}");
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = uccPath,
            Arguments = "make -nobind",
            WorkingDirectory = _interfaceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };
        
        var warnings = new List<string>();
        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var line = e.Data;
            var isError = line.Contains("Error,", StringComparison.OrdinalIgnoreCase) ||
                          line.Contains("Compile aborted", StringComparison.OrdinalIgnoreCase) ||
                          line.Contains("Failure -", StringComparison.OrdinalIgnoreCase);

            var isWarning = line.Contains("warning", StringComparison.OrdinalIgnoreCase);

            if (isError)
            {
                if (line.Contains(": Error,", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split([": Error,"], StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        var filePath = parts[0].Trim();

                        var basePath = Path.GetDirectoryName(_interfaceDir);
                        if (!string.IsNullOrEmpty(basePath))
                        {
                            basePath = basePath.TrimEnd('\\', '/') + "\\";
                            if (filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                            {
                                filePath = filePath.Substring(basePath.Length);
                            }
                        }
                        
                        if (filePath.StartsWith("Interface\\", StringComparison.OrdinalIgnoreCase))
                        {
                            filePath = filePath.Substring("Interface\\".Length);
                        }

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($" {filePath}: Error,{parts[1]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($" {line}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($" {line}");
                    Console.ResetColor();
                }

                warnings.Add(line);
            }
            else if (isWarning)
            {
                if (line.Contains(": Warning,", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split([": Warning,"], StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        var filePath = parts[0].Trim();

                        var basePath = Path.GetDirectoryName(_interfaceDir);
                        if (!string.IsNullOrEmpty(basePath))
                        {
                            basePath = basePath.TrimEnd('\\', '/') + "\\";
                            if (filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                            {
                                filePath = filePath.Substring(basePath.Length);
                            }
                        }
                        
                        if (filePath.StartsWith("Interface\\", StringComparison.OrdinalIgnoreCase))
                        {
                            filePath = filePath.Substring("Interface\\".Length);
                        }

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($" {filePath}: Warning,{parts[1]}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($" {line}");
                    }

                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($" {line}");
                    Console.ResetColor();
                }

                warnings.Add(line);
            }
            else
            {
                Console.WriteLine($" {line}");
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var line = e.Data;
            var errorLine = $"[ERROR] {line}";
            warnings.Add(errorLine);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($" {line}");
            Console.ResetColor();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        SaveCompilationLog(warnings);

        if (process.ExitCode != 0)
        {
            throw new Exception($"Compilação falhou com código de saída: {process.ExitCode}");
        }

        Console.WriteLine(" Compilação concluída");
    }

    private static void DeleteCompiledFiles()
    {
        if (_interfaceDir == null) return;
        if (_deleteFiles == null || _deleteFiles.Length == 0) return;
        foreach (var fileName in _deleteFiles)
        {
            var filePath = Path.Combine(_interfaceDir, fileName);

            if (!File.Exists(filePath)) continue;
            File.Delete(filePath);
        }
    }

    private static void SaveCompilationLog(List<string> warnings)
    {
        try
        {
            var exeDirectory = AppContext.BaseDirectory;
            var logPath = Path.Combine(exeDirectory, "log.txt");

            if (warnings.Count <= 0) return;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using var writer = new StreamWriter(logPath, false);
            writer.WriteLine("========================================");
            writer.WriteLine($"Log de Compilação - {timestamp}");
            writer.WriteLine("========================================\n");
            writer.WriteLine("=== WARNINGS E ERROS ===\n");

            foreach (var warning in warnings)
            {
                writer.WriteLine(warning);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao salvar log: {ex.Message}");
            Console.WriteLine($"Detalhes: {ex.StackTrace}");
        }
    }

    private static void CopyCompiledFile()
    {
        Console.WriteLine(" Copiando arquivo compilado...");
        if (_interfaceDir == null) return;
        var sourceFile = Path.Combine(_interfaceDir, "interface.u");
        if (_clientDir == null) return;
        var destFile = Path.Combine(_clientDir, "interface.u");

        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException($"Arquivo compilado não encontrado: {sourceFile}");
        }

        if (!Directory.Exists(_clientDir))
        {
            throw new DirectoryNotFoundException($"Diretório de destino não encontrado: {_clientDir}");
        }

        if (File.Exists(destFile))
        {
            var fileInfo = new FileInfo(destFile);
            if (fileInfo.IsReadOnly)
            {
                fileInfo.IsReadOnly = false;
            }
        }

        File.Copy(sourceFile, destFile, true);

        var info = new FileInfo(destFile);
        Console.WriteLine($"Arquivo copiado ({info.Length:N0} bytes)");
    }

    private static void StartL2()
    {
        Console.WriteLine(" Iniciando L2.exe...");

        if (_clientDir != null)
        {
            var l2Path = Path.Combine(_clientDir, "l2.exe");

            if (!File.Exists(l2Path))
            {
                throw new FileNotFoundException($"L2.exe não encontrado: {l2Path}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = l2Path,
                WorkingDirectory = _clientDir,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        Console.WriteLine("L2.exe iniciado");
    }
}