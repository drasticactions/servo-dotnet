fn main() {
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if target_os == "android" {
        let out = std::env::var("OUT_DIR").unwrap();
        let out = std::path::Path::new(&out);
        std::fs::write(out.join("libgcc.a"), b"INPUT(-lunwind)").unwrap();
        println!("cargo:rustc-link-search=native={}", out.display());
    }

    if target_os == "linux" {
        println!("cargo:rustc-cdylib-link-arg=-Wl,-soname,libservo_ffi.so");
    }

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/types.rs")
        .csharp_class_name("ServoNative")
        .csharp_namespace("Servo")
        .csharp_dll_name("servo_ffi")
        .csharp_class_accessibility("internal")
        .csharp_use_function_pointer(true)
        .csharp_use_nint_types(true)
        .generate_csharp_file("../src/Servo/ServoNative.g.cs")
        .unwrap();
}
