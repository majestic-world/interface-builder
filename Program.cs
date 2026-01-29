using System.Diagnostics;

namespace InterfaceBuilder;

internal static class InterfaceCompiler
{

    private static readonly Dictionary<string, string?> Config =  new();
    
    private static string? _batchFile;
    private static string? _interfaceDir;
    private static string? _clientDir;

    private static void Main(string[] args)
    {
        var lines = File.ReadAllLines("config.properties");
        foreach (var line in lines)
        {
            var split = line.Split('=');
            var key = split[0].Trim();
            var value = split[1].Trim();
            Config.Add(key, value);
        }

        _batchFile = Config["BatchFile"];
        _interfaceDir = Config["InterfaceDir"];
        _clientDir = Config["ClientDir"];
        
        Console.WriteLine("=================================");
        Console.WriteLine("Interface Compiler & Auto-Restart");
        Console.WriteLine("=================================\n");

        try
        {
            CloseL2Process();
            
            CompileInterface();

            CopyCompiledFile();

            StartL2();

            Console.WriteLine("================================");
            Console.WriteLine("Processo concluído com sucesso!");
            Console.WriteLine("================================");
            
            //Console.ReadKey();
            //Thread.Sleep(20000);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERRO] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void CloseL2Process()
    {
        Console.WriteLine("[1/4] Verificando processo L2.exe...");

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
                Console.WriteLine($"Erro ao fechar PID {process.Id}: {ex.Message}");
            }
        }
    }

    private static void CompileInterface()
    {
        Console.WriteLine("\n[2/4] Compilando interface...");

        if (_interfaceDir != null)
        {
            if (_batchFile != null)
            {
                var batchPath = Path.Combine(_interfaceDir, _batchFile);

                if (!File.Exists(batchPath))
                {
                    throw new FileNotFoundException($"Arquivo batch não encontrado: {batchPath}");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    WorkingDirectory = _interfaceDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                var logLines = new List<string>();
                var warnings = new List<string>();
                
                using var process = new Process();
                process.StartInfo = startInfo;
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    
                    var line = e.Data;
                    logLines.Add(line);
                    Console.WriteLine($"      {line}");
                    
                    // Detecta warnings (case-insensitive)
                    if (line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("aviso", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add(line);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    
                    var line = e.Data;
                    var errorLine = $"[ERROR] {line}";
                    logLines.Add(errorLine);
                    
                    // Todos os erros são considerados warnings também
                    warnings.Add(errorLine);
                    
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"      {line}");
                    Console.ResetColor();
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                // Salva o log completo e os warnings
                SaveCompilationLog(logLines, warnings);

                if (process.ExitCode != 0)
                {
                    Console.WriteLine("Pressione qualquer tecla para sair...");
                    Console.ReadKey();
                    throw new Exception($"Compilação falhou com código de saída: {process.ExitCode}");
                }
            }
        }

        Console.WriteLine("\nCompilação concluída");
    }

    private static void SaveCompilationLog(List<string> allLines, List<string> warnings)
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
        Console.WriteLine("\n[3/4] Copiando arquivo compilado...");

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
        Console.WriteLine("\n[4/4] Iniciando L2.exe...");

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

        Console.WriteLine("L2.exe iniciado\n");
    }
}