//! Pure helpers for running the `tyrant rpc` sidecar; kept free of Tauri so they can be unit-tested.

use std::collections::VecDeque;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use serde::Serialize;

/// Payload of the `tyrant://exit` event.
#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExitPayload {
    pub code: Option<i32>,
    pub restarting: bool,
    pub error: Option<String>,
}

/// Allows at most `max` restarts inside a sliding `window`; a core that keeps crashing stays down.
pub struct RestartPolicy {
    max: usize,
    window: Duration,
    recent: VecDeque<Instant>,
}

impl RestartPolicy {
    pub fn new(max: usize, window: Duration) -> Self {
        Self { max, window, recent: VecDeque::new() }
    }

    pub fn allow(&mut self, now: Instant) -> bool {
        while let Some(&oldest) = self.recent.front() {
            if now.duration_since(oldest) > self.window {
                self.recent.pop_front();
            } else {
                break;
            }
        }
        if self.recent.len() >= self.max {
            return false;
        }
        self.recent.push_back(now);
        true
    }
}

/// Where tyrant.exe lives: `TYRANT_CLI` wins; release builds use the bundled copy; debug builds use the CLI's build output.
pub fn resolve_cli(env_override: Option<PathBuf>, resource_dir: Option<PathBuf>, manifest_dir: &Path, debug: bool) -> PathBuf {
    if let Some(path) = env_override {
        return path;
    }
    if !debug {
        if let Some(resources) = resource_dir {
            return resources.join("sidecar").join("tyrant.exe");
        }
    }
    manifest_dir.join("..").join("..").join("cli").join("Tyrant.Cli").join("bin").join("Debug").join("net10.0").join("tyrant.exe")
}

/// Calls `on_line` for every line (LF or CRLF), decoding invalid UTF-8 lossily so a bad byte never stops the reader.
pub fn read_lines(reader: impl Read, mut on_line: impl FnMut(String)) {
    use std::io::BufRead;
    let mut reader = std::io::BufReader::new(reader);
    let mut buffer = Vec::new();
    loop {
        buffer.clear();
        match reader.read_until(b'\n', &mut buffer) {
            Ok(0) | Err(_) => break,
            Ok(_) => {
                while matches!(buffer.last(), Some(b'\n' | b'\r')) {
                    buffer.pop();
                }
                on_line(String::from_utf8_lossy(&buffer).into_owned());
            }
        }
    }
}

/// Moves a log larger than `max_bytes` to `<name>.log.old` so it never grows without bound.
pub fn rotate_if_large(path: &Path, max_bytes: u64) {
    if let Ok(meta) = std::fs::metadata(path) {
        if meta.len() > max_bytes {
            let _ = std::fs::rename(path, path.with_extension("log.old"));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn restart_policy_allows_three_restarts_a_minute() {
        let mut policy = RestartPolicy::new(3, Duration::from_secs(60));
        let t0 = Instant::now();
        assert!(policy.allow(t0));
        assert!(policy.allow(t0 + Duration::from_secs(1)));
        assert!(policy.allow(t0 + Duration::from_secs(2)));
        assert!(!policy.allow(t0 + Duration::from_secs(3)));
        assert!(policy.allow(t0 + Duration::from_secs(62)));
    }

    #[test]
    fn env_override_wins() {
        let cli = resolve_cli(Some(PathBuf::from("C:/x/tyrant.exe")), Some(PathBuf::from("C:/res")), Path::new("C:/repo/studio/src-tauri"), false);
        assert_eq!(cli, PathBuf::from("C:/x/tyrant.exe"));
    }

    #[test]
    fn release_builds_use_the_bundled_cli() {
        let cli = resolve_cli(None, Some(PathBuf::from("C:/res")), Path::new("C:/repo/studio/src-tauri"), false);
        assert_eq!(cli, PathBuf::from("C:/res").join("sidecar").join("tyrant.exe"));
    }

    #[test]
    fn debug_builds_use_the_cli_build_output() {
        let cli = resolve_cli(None, Some(PathBuf::from("C:/res")), Path::new("C:/repo/studio/src-tauri"), true);
        assert!(cli.ends_with(Path::new("cli/Tyrant.Cli/bin/Debug/net10.0/tyrant.exe")), "{}", cli.display());
    }

    #[test]
    fn read_lines_splits_lf_and_crlf_and_survives_invalid_utf8() {
        let input: &[u8] = b"one\r\ntwo\nbad\xff\nlast";
        let mut lines = Vec::new();
        read_lines(input, |line| lines.push(line));
        assert_eq!(lines, vec!["one", "two", "bad\u{fffd}", "last"]);
    }

    #[test]
    fn large_logs_are_rotated() {
        let dir = std::env::temp_dir().join(format!("tyrant-rotate-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let log = dir.join("core.log");
        std::fs::write(&log, vec![b'x'; 100]).unwrap();

        rotate_if_large(&log, 10);

        assert!(!log.exists());
        assert!(dir.join("core.log.old").exists());
        rotate_if_large(&dir.join("missing.log"), 10); // no file: nothing happens
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn exit_payload_is_camel_case_json() {
        let json = serde_json::to_string(&ExitPayload { code: Some(1), restarting: true, error: None }).unwrap();
        assert_eq!(json, r#"{"code":1,"restarting":true,"error":null}"#);
    }
}
