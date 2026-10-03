//! Runs `tyrant rpc` as a child process and relays its stdout lines to the UI as events. The Rust layer never parses
//! the protocol (spec §2.2: it only manages the window, dialogs and the sidecar).

use std::fs::OpenOptions;
use std::io::Write;
use std::path::PathBuf;
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use tauri::{AppHandle, Emitter};

use crate::support::{read_lines, rotate_if_large, ExitPayload, RestartPolicy};

pub const LINE_EVENT: &str = "tyrant://line";
pub const EXIT_EVENT: &str = "tyrant://exit";
pub const STARTED_EVENT: &str = "tyrant://started";
const MAX_LOG_BYTES: u64 = 5 * 1024 * 1024;

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

pub struct Sidecar {
    cli: PathBuf,
    log_path: Option<PathBuf>,
    stdin: Mutex<Option<ChildStdin>>,
    child: Mutex<Option<Child>>,
    last_error: Mutex<Option<String>>,
    policy: Mutex<RestartPolicy>,
    shutting_down: AtomicBool,
}

impl Sidecar {
    pub fn start(app: &AppHandle, cli: PathBuf, log_path: Option<PathBuf>) -> Arc<Self> {
        if let Some(path) = &log_path {
            rotate_if_large(path, MAX_LOG_BYTES);
        }
        let sidecar = Arc::new(Self {
            cli,
            log_path,
            stdin: Mutex::new(None),
            child: Mutex::new(None),
            last_error: Mutex::new(None),
            policy: Mutex::new(RestartPolicy::new(3, Duration::from_secs(60))),
            shutting_down: AtomicBool::new(false),
        });
        sidecar.spawn(app.clone());
        sidecar
    }

    fn spawn(self: &Arc<Self>, app: AppHandle) {
        if self.shutting_down.load(Ordering::SeqCst) {
            return; // the app is exiting: never start a new core
        }
        let mut command = Command::new(&self.cli);
        command.arg("rpc").stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::piped());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x0800_0000;
            command.creation_flags(CREATE_NO_WINDOW);
        }
        let mut child = match command.spawn() {
            Ok(child) => child,
            Err(err) => {
                let message = format!(
                    "Could not start the Tyrant core ({}): {err}. In development, build it first with 'dotnet build Tyrant.slnx'.",
                    self.cli.display()
                );
                *lock(&self.last_error) = Some(message.clone());
                let _ = app.emit(EXIT_EVENT, ExitPayload { code: None, restarting: false, error: Some(message) });
                return;
            }
        };
        let stdout = child.stdout.take().expect("stdout is piped");
        let stderr = child.stderr.take().expect("stderr is piped");
        *lock(&self.stdin) = child.stdin.take();
        *lock(&self.child) = Some(child);
        *lock(&self.last_error) = None;
        let _ = app.emit(STARTED_EVENT, ());

        let log_path = self.log_path.clone();
        std::thread::spawn(move || {
            let mut file = log_path.and_then(|p| OpenOptions::new().create(true).append(true).open(p).ok());
            read_lines(stderr, |line| match file.as_mut() {
                Some(f) => {
                    let _ = writeln!(f, "{line}");
                }
                None => eprintln!("[tyrant] {line}"),
            });
        });

        let me = Arc::clone(self);
        std::thread::spawn(move || {
            read_lines(stdout, |line| {
                let _ = app.emit(LINE_EVENT, line);
            });
            me.exited(app);
        });
    }

    fn exited(self: &Arc<Self>, app: AppHandle) {
        *lock(&self.stdin) = None;
        let child = lock(&self.child).take();
        let code = child.and_then(|mut c| c.wait().ok()).and_then(|status| status.code());
        if self.shutting_down.load(Ordering::SeqCst) {
            return;
        }
        let restarting = lock(&self.policy).allow(Instant::now());
        let error = if restarting {
            None
        } else {
            let message = format!(
                "The Tyrant core stopped (exit code {}) and keeps crashing, so it was not restarted. Restart Tyrant; the log is core.log in the app's log folder.",
                code.map_or_else(|| "unknown".to_string(), |c| c.to_string())
            );
            *lock(&self.last_error) = Some(message.clone());
            Some(message)
        };
        let _ = app.emit(EXIT_EVENT, ExitPayload { code, restarting, error });
        if restarting {
            std::thread::sleep(Duration::from_millis(500));
            if !self.shutting_down.load(Ordering::SeqCst) {
                self.spawn(app); // the app may have started exiting during the pause
            }
        }
    }

    /// Writes one request line to the core's stdin.
    pub fn send(&self, line: &str) -> Result<(), String> {
        let mut guard = lock(&self.stdin);
        let Some(stdin) = guard.as_mut() else {
            return Err(lock(&self.last_error).clone().unwrap_or_else(|| "The Tyrant core is restarting; try again in a moment.".into()));
        };
        stdin
            .write_all(line.as_bytes())
            .and_then(|()| stdin.write_all(b"\n"))
            .and_then(|()| stdin.flush())
            .map_err(|e| format!("Could not talk to the Tyrant core: {e}"))
    }

    /// Closes stdin so the core cancels its running job and exits; kills it if it is still running after `grace`.
    pub fn shutdown(&self, grace: Duration) {
        self.shutting_down.store(true, Ordering::SeqCst);
        *lock(&self.stdin) = None;
        let deadline = Instant::now() + grace;
        loop {
            {
                let mut guard = lock(&self.child);
                let Some(child) = guard.as_mut() else { return };
                match child.try_wait() {
                    Ok(Some(_)) | Err(_) => return,
                    Ok(None) if Instant::now() >= deadline => {
                        let _ = child.kill();
                        return;
                    }
                    Ok(None) => {}
                }
            }
            std::thread::sleep(Duration::from_millis(100));
        }
    }
}
