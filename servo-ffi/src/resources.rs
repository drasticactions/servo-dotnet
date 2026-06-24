use std::fs;
use std::path::PathBuf;
use std::sync::OnceLock;

use servo::resources::{Resource, ResourceReaderMethods};

pub static RESOURCE_READER: EmbeddedResourceReader = EmbeddedResourceReader {
    resource_dir: OnceLock::new(),
};

servo::submit_resource_reader!(&RESOURCE_READER);

pub struct EmbeddedResourceReader {
    resource_dir: OnceLock<PathBuf>,
}

impl EmbeddedResourceReader {
    pub fn init_from_path(&self, path: PathBuf) {
        self.resource_dir
            .set(path)
            .expect("Resource reader already initialized");
    }

    pub fn init_from_exe_dir(&self) {
        let exe_path = std::env::current_exe().expect("Failed to get executable path");
        let exe_dir = exe_path
            .parent()
            .expect("Executable has no parent directory");

        let mut dir = exe_dir.to_path_buf();
        loop {
            let candidate = dir.join("resources");
            if candidate.is_dir() {
                let _ = self.resource_dir.set(candidate);
                return;
            }
            if !dir.pop() {
                break;
            }
        }

        let cwd = std::env::current_dir().unwrap_or_default();
        let candidate = cwd.join("resources");
        if candidate.is_dir() {
            let _ = self.resource_dir.set(candidate);
            return;
        }
        let fallback = exe_dir.join("resources");
        eprintln!(
            "servo-ffi: no Servo resources directory found near {} or CWD {}; \
             falling back to bundled defaults",
            exe_dir.display(),
            cwd.display()
        );
        let _ = self.resource_dir.set(fallback);
    }

    fn resource_dir(&self) -> &PathBuf {
        self.resource_dir
            .get()
            .expect("Resource reader not initialized. Call servo_new() first.")
    }
}

impl ResourceReaderMethods for EmbeddedResourceReader {
    fn read(&self, res: Resource) -> Vec<u8> {
        let path = self.resource_dir().join(res.filename());
        fs::read(&path).unwrap_or_else(|e| {
            eprintln!(
                "servo-ffi: failed to read resource {}: {e}",
                path.display()
            );
            Vec::new()
        })
    }

    fn sandbox_access_files(&self) -> Vec<PathBuf> {
        vec![]
    }

    fn sandbox_access_files_dirs(&self) -> Vec<PathBuf> {
        vec![self.resource_dir().clone()]
    }
}
