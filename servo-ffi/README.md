# servo-ffi

servo-ffi is an experimental, exposed ffi surface for [servo](../external/servo/), with additional code to handle usecases for the [ServoDotNet](../src/Servo/) library. This was started before the servo-capi crate was created. As of this writing (24-06-2026) [that ffi](../external/servo/ffi/capi/) does not have enough functions exposed yet to match what this does yet, but hopefully it will. I rather not maintain this code, given the option.

## How to build

Run the build scripts in the root of the repo.