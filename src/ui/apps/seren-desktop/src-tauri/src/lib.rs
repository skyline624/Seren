use tauri::Manager;

#[tauri::command]
fn toggle_always_on_top(app: tauri::AppHandle) -> Result<bool, String> {
    let window = app
        .get_webview_window("main")
        .ok_or("main window not found")?;

    let current = window.is_always_on_top().map_err(|e| e.to_string())?;
    let next = !current;
    window
        .set_always_on_top(next)
        .map_err(|e| e.to_string())?;
    Ok(next)
}

#[tauri::command]
async fn import_vrm_file(app: tauri::AppHandle) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let file = app
        .dialog()
        .file()
        .add_filter("VRM Models", &["vrm", "glb", "gltf"])
        .blocking_pick_file();

    Ok(file.map(|f| f.path.to_string_lossy().to_string()))
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .invoke_handler(tauri::generate_handler![
            toggle_always_on_top,
            import_vrm_file,
        ])
        .setup(|app| {
            #[cfg(desktop)]
            {
                use tauri::tray::{
                    MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent,
                };
                use tauri::menu::{MenuBuilder, MenuItemBuilder};

                let show_item = MenuItemBuilder::with_id("show", "Show Seren").build(app)?;
                let on_top_item =
                    MenuItemBuilder::with_id("toggle_on_top", "Always on Top").build(app)?;
                let quit_item = MenuItemBuilder::with_id("quit", "Quit").build(app)?;

                let menu = MenuBuilder::new(app)
                    .items(&[&show_item, &on_top_item, &quit_item])
                    .build()?;

                let _tray = TrayIconBuilder::new()
                    .icon(app.default_window_icon().unwrap().clone())
                    .tooltip("Seren")
                    .menu(&menu)
                    .on_menu_event(|app, event| match event.id().as_ref() {
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.show();
                                let _ = window.set_focus();
                            }
                        }
                        "toggle_on_top" => {
                            if let Some(window) = app.get_webview_window("main") {
                                if let Ok(current) = window.is_always_on_top() {
                                    let _ = window.set_always_on_top(!current);
                                }
                            }
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    })
                    .on_tray_icon_event(|tray, event| match event {
                        TrayIconEvent::Click {
                            button: MouseButton::Left,
                            button_state: MouseButtonState::Up,
                            ..
                        } => {
                            let app = tray.app_handle();
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.show();
                                let _ = window.set_focus();
                            }
                        }
                        _ => {}
                    })
                    .build(app)?;
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
