#![cfg_attr(windows, windows_subsystem = "windows")]

use serde::Deserialize;
use sha2::{Digest, Sha256};
use std::{
    fs::{self, File, OpenOptions},
    io,
    path::PathBuf,
};

#[cfg(windows)]
use std::path::Path;

#[cfg(windows)]
mod windows;

type Result<T> = std::result::Result<T, Box<dyn std::error::Error>>;

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Request {
    executable: String,
    working_directory: String,
    arguments: String,
    dlls: Vec<Dll>,
    server: bool,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Dll {
    path: String,
    size: u64,
    sha256: String,
}

fn verified_files(dlls: &[Dll]) -> Result<Vec<File>> {
    dlls.iter()
        .map(|dll| {
            let mut options = OpenOptions::new();
            options.read(true);
            #[cfg(windows)]
            {
                use std::os::windows::fs::OpenOptionsExt;
                options.share_mode(1); // FILE_SHARE_READ: deny replacement while the game uses it.
            }
            let mut file = options.open(&dll.path)?;
            if file.metadata()?.len() != dll.size {
                return Err("DLL size mismatch".into());
            }
            let mut digest = Sha256::new();
            io::copy(&mut file, &mut digest)?;
            if !format!("{:x}", digest.finalize()).eq_ignore_ascii_case(&dll.sha256) {
                return Err("DLL hash mismatch".into());
            }
            Ok(file)
        })
        .collect()
}

struct Cleanup(PathBuf);

impl Drop for Cleanup {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.0);
        if let Some(directory) = self.0.parent() {
            for name in ["ready", "stop", "heartbeat"] {
                let _ = fs::remove_file(directory.join(name));
            }
            let _ = fs::remove_dir(directory);
        }
    }
}

#[cfg(windows)]
fn run() -> Result<u32> {
    let args: Vec<_> = std::env::args_os().skip(1).collect();
    if args.len() != 2 {
        return Ok(2);
    }
    let path = Path::new(&args[1]);
    if args[0] == "local-app-data" {
        fs::write(path, serde_json::to_vec(&windows::local_app_data()?)?)?;
        return Ok(0);
    }
    if args[0] != "launch" {
        return Ok(2);
    }
    let _cleanup = Cleanup(path.to_owned());
    let request: Request = serde_json::from_slice(&fs::read(path)?)?;
    fs::remove_file(path)?;
    let _files = verified_files(&request.dlls)?;
    windows::launch(&request, path.parent().ok_or("Missing request directory")?)
}

fn main() {
    #[cfg(windows)]
    let code = run().unwrap_or(1);
    #[cfg(not(windows))]
    let code: u32 = 2;
    // Never print launch arguments or errors: either may contain credentials.
    std::process::exit(code as i32);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_launchers_pascal_case_request() {
        let request: Request = serde_json::from_str(r#"{"Executable":"C:\\game.exe","WorkingDirectory":"C:\\game","Arguments":"-server","Dlls":[{"Path":"C:\\plugin.dll","Size":42,"Sha256":"abcd"}],"Server":true}"#).unwrap();
        assert_eq!(request.executable, r"C:\game.exe");
        assert_eq!(request.working_directory, r"C:\game");
        assert_eq!(request.arguments, "-server");
        assert!(request.server);
        assert_eq!(request.dlls[0].size, 42);
    }

    #[test]
    fn validates_size_and_hash_before_launch() {
        let path = std::env::temp_dir().join(format!("unchained-dll-check-{}", std::process::id()));
        fs::write(&path, b"verified bytes").unwrap();
        let mut dll = Dll {
            path: path.to_str().unwrap().into(),
            size: 14,
            sha256: format!("{:X}", Sha256::digest(b"verified bytes")),
        };
        assert!(verified_files(std::slice::from_ref(&dll)).is_ok());
        dll.size += 1;
        assert!(verified_files(std::slice::from_ref(&dll)).is_err());
        dll.size -= 1;
        dll.sha256 = "0".repeat(64);
        assert!(verified_files(&[dll]).is_err());
        fs::remove_file(path).unwrap();
    }

    #[test]
    fn cleanup_removes_only_launch_files_not_unrelated_contents() {
        let directory =
            std::env::temp_dir().join(format!("unchained-cleanup-{}", std::process::id()));
        fs::create_dir_all(&directory).unwrap();
        for name in ["request.json", "ready", "heartbeat", "stop", "unrelated"] {
            fs::write(directory.join(name), "").unwrap();
        }
        drop(Cleanup(directory.join("request.json")));
        assert_eq!(fs::read_dir(&directory).unwrap().count(), 1);
        fs::remove_file(directory.join("unrelated")).unwrap();
        fs::remove_dir(directory).unwrap();
    }
}
