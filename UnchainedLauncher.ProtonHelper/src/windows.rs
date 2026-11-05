use super::{Request, Result};
use std::{
    ffi::c_void,
    fs,
    mem::size_of,
    path::Path,
    time::{Duration, SystemTime},
};
use windows::{
    core::{s, w, PCWSTR, PWSTR},
    Win32::{
        Foundation::*,
        Security::SECURITY_ATTRIBUTES,
        System::{
            Com::CoTaskMemFree, Console::*, Diagnostics::Debug::WriteProcessMemory, JobObjects::*,
            LibraryLoader::*, Memory::*, Pipes::CreatePipe, Threading::*,
        },
        UI::Shell::{FOLDERID_LocalAppData, SHGetKnownFolderPath, KF_FLAG_DEFAULT},
    },
};

struct Handle(HANDLE);

impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

fn wide(text: &str) -> Result<Vec<u16>> {
    if text.contains('\0') {
        return Err("NUL in Windows argument".into());
    }
    Ok(text.encode_utf16().chain(Some(0)).collect())
}

pub(super) fn local_app_data() -> Result<String> {
    unsafe {
        let path = SHGetKnownFolderPath(&FOLDERID_LocalAppData, KF_FLAG_DEFAULT, None)?;
        let result = path.to_string();
        CoTaskMemFree(Some(path.0.cast()));
        Ok(result?)
    }
}

struct Stdio {
    input: Handle,
    output: Handle,
    error: Handle,
}

impl Stdio {
    unsafe fn new() -> Result<Self> {
        fn duplicate(kind: STD_HANDLE) -> Result<Handle> {
            unsafe {
                let mut handle = HANDLE::default();
                let process = GetCurrentProcess();
                DuplicateHandle(
                    process,
                    GetStdHandle(kind)?,
                    process,
                    &mut handle,
                    0,
                    true,
                    DUPLICATE_SAME_ACCESS,
                )?;
                Ok(Handle(handle))
            }
        }
        let attributes = SECURITY_ATTRIBUTES {
            nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
            bInheritHandle: true.into(),
            ..Default::default()
        };
        let (mut read, mut write) = (HANDLE::default(), HANDLE::default());
        CreatePipe(&mut read, &mut write, Some(&attributes), 0)?;
        let input = Handle(read);
        drop(Handle(write)); // Captured stdin is EOF, never the TUI keyboard.
        Ok(Self {
            input,
            output: duplicate(STD_OUTPUT_HANDLE)?,
            error: duplicate(STD_ERROR_HANDLE)?,
        })
    }
}

struct Attributes {
    storage: Vec<usize>,
    initialized: bool,
}

impl Attributes {
    unsafe fn new(handles: &[HANDLE]) -> Result<Self> {
        let mut bytes = 0;
        let _ = InitializeProcThreadAttributeList(
            LPPROC_THREAD_ATTRIBUTE_LIST::default(),
            1,
            0,
            &mut bytes,
        );
        if bytes == 0 {
            return Err(windows::core::Error::from_win32().into());
        }
        let mut value = Self {
            storage: vec![0; bytes.div_ceil(size_of::<usize>())],
            initialized: false,
        };
        InitializeProcThreadAttributeList(value.pointer(), 1, 0, &mut bytes)?;
        value.initialized = true;
        UpdateProcThreadAttribute(
            value.pointer(),
            0,
            PROC_THREAD_ATTRIBUTE_HANDLE_LIST as usize,
            Some(handles.as_ptr().cast()),
            std::mem::size_of_val(handles),
            None,
            None,
        )?;
        Ok(value)
    }

    fn pointer(&mut self) -> LPPROC_THREAD_ATTRIBUTE_LIST {
        LPPROC_THREAD_ATTRIBUTE_LIST(self.storage.as_mut_ptr().cast())
    }
}

impl Drop for Attributes {
    fn drop(&mut self) {
        if self.initialized {
            unsafe {
                DeleteProcThreadAttributeList(self.pointer());
            }
        }
    }
}

struct Game {
    process: Handle,
    thread: Handle,
    job: Handle,
    finished: bool,
}

impl Drop for Game {
    fn drop(&mut self) {
        if !self.finished {
            unsafe {
                let _ = TerminateJobObject(self.job.0, 1);
                let _ = TerminateProcess(self.process.0, 1);
                let _ = WaitForSingleObject(self.process.0, 5000);
            }
        }
    }
}

unsafe fn inject(process: HANDLE, path: &str) -> Result<()> {
    let path = wide(path)?;
    let bytes = path.len() * size_of::<u16>();
    let address = VirtualAllocEx(
        process,
        None,
        bytes,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_READWRITE,
    );
    if address.is_null() {
        return Err(windows::core::Error::from_win32().into());
    }
    let result = (|| -> Result<()> {
        let mut written = 0;
        WriteProcessMemory(
            process,
            address,
            path.as_ptr().cast(),
            bytes,
            Some(&mut written),
        )?;
        if written != bytes {
            return Err("Incomplete DLL path write".into());
        }
        let load = GetProcAddress(GetModuleHandleW(w!("kernel32.dll"))?, s!("LoadLibraryW"))
            .ok_or("Missing LoadLibraryW")?;
        let thread = Handle(CreateRemoteThread(
            process,
            None,
            0,
            Some(std::mem::transmute::<
                unsafe extern "system" fn() -> isize,
                unsafe extern "system" fn(*mut c_void) -> u32,
            >(load)),
            Some(address),
            0,
            None,
        )?);
        if WaitForSingleObject(thread.0, INFINITE) != WAIT_OBJECT_0 {
            return Err("DLL load wait failed".into());
        }
        let mut result = 0;
        GetExitCodeThread(thread.0, &mut result)?;
        if result == 0 {
            return Err("DLL load failed".into());
        }
        Ok(())
    })();
    if result.is_err() {
        // Do not release the remote path while a failed load might still use it.
        let _ = TerminateProcess(process, 1);
        let _ = WaitForSingleObject(process, 5000);
    }
    let _ = VirtualFreeEx(process, address, 0, MEM_RELEASE);
    result
}

pub(super) fn launch(request: &Request, directory: &Path) -> Result<u32> {
    if !Path::new(&request.executable).is_absolute()
        || !Path::new(&request.working_directory).is_absolute()
        || request
            .dlls
            .iter()
            .any(|dll| !Path::new(&dll.path).is_absolute())
    {
        return Err("Launch paths must be absolute".into());
    }
    let executable = wide(&request.executable)?;
    let working_directory = wide(&request.working_directory)?;
    let mut command = wide(&format!("\"{}\" {}", request.executable, request.arguments))?;
    unsafe {
        let job = Handle(CreateJobObjectW(None, None)?);
        let stdio = if request.server {
            Some(Stdio::new()?)
        } else {
            None
        };
        let handles = stdio
            .as_ref()
            .map(|io| [io.input.0, io.output.0, io.error.0]);
        let mut attributes = handles
            .as_ref()
            .map(|handles| Attributes::new(handles))
            .transpose()?;
        let mut startup = STARTUPINFOEXW::default();
        startup.StartupInfo.cb = size_of::<STARTUPINFOW>() as u32;
        if let (Some(io), Some(attributes)) = (&stdio, &mut attributes) {
            startup.StartupInfo.cb = size_of::<STARTUPINFOEXW>() as u32;
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.StartupInfo.hStdInput = io.input.0;
            startup.StartupInfo.hStdOutput = io.output.0;
            startup.StartupInfo.hStdError = io.error.0;
            startup.lpAttributeList = attributes.pointer();
        }
        let mut native = PROCESS_INFORMATION::default();
        CreateProcessW(
            PCWSTR(executable.as_ptr()),
            PWSTR(command.as_mut_ptr()),
            None,
            None,
            request.server,
            CREATE_SUSPENDED
                | if request.server {
                    CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT
                } else {
                    PROCESS_CREATION_FLAGS(0)
                },
            None,
            PCWSTR(working_directory.as_ptr()),
            &startup.StartupInfo,
            &mut native,
        )?;
        let mut game = Game {
            process: Handle(native.hProcess),
            thread: Handle(native.hThread),
            job,
            finished: false,
        };
        // Track only this game's descendants, never a prefix-wide wineserver.
        AssignProcessToJobObject(game.job.0, game.process.0)?;
        drop(attributes);
        drop(stdio);
        for dll in &request.dlls {
            inject(game.process.0, &dll.path)?;
        }
        if ResumeThread(game.thread.0) != 1 {
            return Err("Unexpected game suspend count".into());
        }
        fs::write(directory.join("ready"), native.dwProcessId.to_string())?;
        loop {
            match WaitForSingleObject(game.process.0, 250) {
                WAIT_OBJECT_0 => {
                    let mut code = 0;
                    GetExitCodeProcess(game.process.0, &mut code)?;
                    game.finished = true;
                    return Ok(code);
                }
                WAIT_TIMEOUT => {}
                _ => return Err("Game wait failed".into()),
            }
            let stale = request.server
                && fs::metadata(directory.join("heartbeat"))
                    .and_then(|file| file.modified())
                    .map(|time| {
                        SystemTime::now().duration_since(time).unwrap_or_default()
                            > Duration::from_secs(20)
                    })
                    .unwrap_or(true);
            if directory.join("stop").exists() || stale {
                return Ok(0);
            }
        }
    }
}
