use std::collections::HashMap;
use std::env;
use std::fs::{self, File};
use std::io::{self, BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use sysinfo::System;
use colored::*;

fn main() {
    #[cfg(windows)]
    let _ = colored::control::set_virtual_terminal(true);

    print!("\x1b]0;Interface Build Assistant\x07");
    
    let start_time = std::time::Instant::now();

    println!(" =================================");
    println!(" Interface Builder Launcher By Mk");
    println!(" =================================");

    let config = match load_config() {
        Ok(c) => c,
        Err(e) => {
            eprintln!(" Error loading config.properties: {}", e);
            pause();
            return;
        }
    };

    let interface_dir = match config.get("InterfaceDir") {
        Some(dir) => dir.clone(),
        None => {
            eprintln!(" InterfaceDir not configured");
            pause();
            return;
        }
    };

    let client_dir = match config.get("ClientDir") {
        Some(dir) => dir.clone(),
        None => {
            eprintln!(" ClientDir not configured");
            pause();
            return;
        }
    };

    let delete_files: Vec<String> = config
        .get("DeleteFiles")
        .map(|s| s.split(',').map(|f| f.trim().to_string()).filter(|s| !s.is_empty()).collect())
        .unwrap_or_default();

    if let Err(e) = run_process(&interface_dir, &client_dir, &delete_files) {
        eprintln!("\n Error: {}", e);
        pause();
    } else {
        println!(" ================================");
        println!(" Process completed successfully!");
        println!(" Compilation time: {:.2}s", start_time.elapsed().as_secs_f64());
        println!(" ================================");
        println!(" Auto-closing in 5 seconds...");
        std::thread::sleep(std::time::Duration::from_secs(5));
    }
}

fn load_config() -> io::Result<HashMap<String, String>> {
    let mut config = HashMap::new();
    let file = File::open("config.properties")?;
    let reader = BufReader::new(file);

    for line_result in reader.lines() {
        let line = line_result?;
        if line.trim().is_empty() {
            continue;
        }
        if let Some((key, value)) = line.split_once('=') {
            config.insert(key.trim().to_string(), value.trim().to_string());
        }
    }
    Ok(config)
}

fn run_process(interface_dir: &str, client_dir: &str, delete_files: &[String]) -> Result<(), Box<dyn std::error::Error>> {
    close_l2_process(client_dir);
    compile_interface(interface_dir, client_dir, delete_files)?;
    copy_compiled_file(interface_dir, client_dir)?;
    start_l2(client_dir)?;
    Ok(())
}

fn close_l2_process(client_dir: &str) {
    println!(" Checking L2.exe process...");
    let mut sys = System::new_all();
    sys.refresh_all();

    let client_path = Path::new(client_dir).canonicalize().unwrap_or_else(|_| PathBuf::from(client_dir));

    for (_pid, process) in sys.processes() {
        let name = process.name().to_lowercase();
        if name == "l2.exe" || name == "l2" {
            if let Some(exe_path) = process.exe() {
                if let Some(process_folder) = exe_path.parent() {
                    let canon_folder = process_folder.canonicalize().unwrap_or_else(|_| process_folder.to_path_buf());
                    if canon_folder == client_path {
                        let _ = process.kill();
                    }
                }
            }
        }
    }
}

fn compile_interface(interface_dir: &str, _client_dir: &str, delete_files: &[String]) -> Result<(), Box<dyn std::error::Error>> {
    println!(" Compiling interface...");
    
    for file_to_delete in delete_files {
        let path = Path::new(interface_dir).join(file_to_delete);
        if path.exists() {
            let _ = fs::remove_file(path);
        }
    }

    let ucc_path = Path::new(interface_dir).join("UCC.exe");
    if !ucc_path.exists() {
        return Err(format!("UCC.exe not found: {}", ucc_path.display()).into());
    }

    let mut child = Command::new(&ucc_path)
        .arg("make")
        .arg("-nobind")
        .current_dir(interface_dir)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;

    let mut warnings = Vec::new();

    if let Some(stdout) = child.stdout.take() {
        let reader = BufReader::new(stdout);
        for line_res in reader.lines() {
            if let Ok(line) = line_res {
                let lower_line = line.to_lowercase();
                let is_error = lower_line.contains("error,") ||
                               lower_line.contains("compile aborted") ||
                               lower_line.contains("failure -");
                let is_warning = lower_line.contains("warning");

                if is_error {
                    println!(" {}", line.red());
                    warnings.push(line);
                } else if is_warning {
                    println!(" {}", line.yellow());
                    warnings.push(line);
                } else {
                    println!(" {}", line);
                }
            }
        }
    }

    if let Some(stderr) = child.stderr.take() {
        let reader = BufReader::new(stderr);
        for line_res in reader.lines() {
            if let Ok(line) = line_res {
                println!(" {}", line.red());
                warnings.push(format!("[ERROR] {}", line));
            }
        }
    }

    let status = child.wait()?;

    save_compilation_log(&warnings);

    if !status.success() {
        return Err(format!("Compilation failed with exit code: {}", status.code().unwrap_or(-1)).into());
    }

    println!(" Compilation completed");
    Ok(())
}

fn save_compilation_log(warnings: &[String]) {
    if warnings.is_empty() {
        return;
    }
    
    if let Ok(exe_path) = env::current_exe() {
        if let Some(exe_dir) = exe_path.parent() {
            let log_path = exe_dir.join("log.txt");
            if let Ok(mut file) = File::create(log_path) {
                let timestamp = chrono::Local::now().format("%Y-%m-%d %H:%M:%S");
                let _ = writeln!(file, "========================================");
                let _ = writeln!(file, "Compilation Log - {}", timestamp);
                let _ = writeln!(file, "========================================\n");
                let _ = writeln!(file, "=== WARNINGS AND ERRORS ===\n");
                
                for warning in warnings {
                    let _ = writeln!(file, "{}", warning);
                }
            }
        }
    }
}

fn copy_compiled_file(interface_dir: &str, client_dir: &str) -> Result<(), Box<dyn std::error::Error>> {
    println!(" Copying compiled file...");
    let source_file = Path::new(interface_dir).join("interface.u");
    let dest_file = Path::new(client_dir).join("interface.u");

    if !source_file.exists() {
        return Err(format!("Compiled file not found: {}", source_file.display()).into());
    }
    if !Path::new(client_dir).exists() {
        return Err(format!("Destination directory not found: {}", client_dir).into());
    }

    if dest_file.exists() {
        let metadata = fs::metadata(&dest_file)?;
        let mut perms = metadata.permissions();
        if perms.readonly() {
            perms.set_readonly(false);
            fs::set_permissions(&dest_file, perms)?;
        }
    }

    fs::copy(&source_file, &dest_file)?;
    
    if let Ok(metadata) = fs::metadata(&dest_file) {
        let size = metadata.len();
        let size_mb = size as f64 / 1_048_576.0;
        if size_mb >= 1.0 {
            println!(" File copied ({:.2} MB)", size_mb);
        } else {
            let size_kb = size as f64 / 1024.0;
            println!(" File copied ({:.2} KB)", size_kb);
        }
    }
    
    Ok(())
}

fn start_l2(client_dir: &str) -> Result<(), Box<dyn std::error::Error>> {
    println!(" Starting L2.exe...");
    let l2_path = Path::new(client_dir).join("l2.exe");

    if !l2_path.exists() {
        return Err(format!("L2.exe not found: {}", l2_path.display()).into());
    }

    Command::new(&l2_path)
        .current_dir(client_dir)
        .spawn()?;

    println!(" L2.exe started");
    Ok(())
}

fn pause() {
    println!(" Press Enter to exit...");
    let mut input = String::new();
    let _ = io::stdin().read_line(&mut input);
}