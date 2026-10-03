// Release builds are windowed apps (no console window).
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tyrant_lib::run()
}
