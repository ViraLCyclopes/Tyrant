mod sidecar;
mod support;

use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use tauri::{Manager, RunEvent, State};

struct CoreState(Arc<sidecar::Sidecar>);

/// The updater's public key (minisign), committed by `npm run updater-key`; empty until the key exists.
const UPDATER_PUBKEY: &str = include_str!("../updater-key.pub");

/// Whether this build can install updates (it has the updater's public key).
#[tauri::command]
fn updater_ready() -> bool {
    support::updater_pubkey(UPDATER_PUBKEY).is_some()
}

/// Sends one JSON-RPC request line to the Tyrant core. Async, so a busy core (full pipe) never blocks the UI thread.
#[tauri::command]
async fn core_send(state: State<'_, CoreState>, line: String) -> Result<(), String> {
    state.0.send(&line)
}

/// Lets the UI load preview files (PNG, .glb) from one workspace's cache\previews folder through the asset protocol.
#[tauri::command]
fn allow_previews(app: tauri::AppHandle, dir: String) -> Result<(), String> {
    let path = std::path::PathBuf::from(&dir);
    if !support::is_preview_dir(&path) {
        return Err(format!("'{dir}' is not a workspace preview folder."));
    }
    std::fs::create_dir_all(&path).map_err(|e| e.to_string())?;
    app.asset_protocol_scope().allow_directory(&path, true).map_err(|e| e.to_string())
}

pub fn run() {
    let mut builder = tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_process::init());
    // Without a key the updater is left out: the app says updates aren't set up in this build instead of failing.
    if let Some(key) = support::updater_pubkey(UPDATER_PUBKEY) {
        builder = builder.plugin(tauri_plugin_updater::Builder::new().pubkey(key).build());
    }
    builder
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
        .invoke_handler(tauri::generate_handler![core_send, allow_previews, updater_ready])
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
