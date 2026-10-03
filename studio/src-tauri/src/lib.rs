mod sidecar;
mod support;

use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use tauri::{Manager, RunEvent, State};

struct CoreState(Arc<sidecar::Sidecar>);

/// Sends one JSON-RPC request line to the Tyrant core.
#[tauri::command]
fn core_send(state: State<'_, CoreState>, line: String) -> Result<(), String> {
    state.0.send(&line)
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let cli = support::resolve_cli(
                std::env::var_os("TYRANT_CLI").map(Into::into),
                app.path().resource_dir().ok(),
                Path::new(env!("CARGO_MANIFEST_DIR")),
                cfg!(debug_assertions),
            );
            let log = app.path().app_log_dir().ok().map(|dir| {
                let _ = std::fs::create_dir_all(&dir);
                dir.join("core.log")
            });
            app.manage(CoreState(sidecar::Sidecar::start(app.handle(), cli, log)));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![core_send])
        .build(tauri::generate_context!())
        .expect("error while building Tyrant")
        .run(|app, event| {
            if let RunEvent::Exit = event {
                if let Some(core) = app.try_state::<CoreState>() {
                    core.0.shutdown(Duration::from_secs(5));
                }
            }
        });
}
