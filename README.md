# servo-dotnet

servo-dotnet is an experimental binding of [Servo](https://servo.org) to .NET.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

## How it works

[servo-ffi](./servo-ffi/) contains a rust library that wraps [servo](./external/servo/) with a custom ffi for building against. That uses csbindgen to create a C# wrapper in the [dotnet](./src/Servo/) library.

The base [dotnet](./src/Servo/) library implements the boilerplate over the ffi library to create the base level controls.

The [Avalonia](./src/Servo.AvaloniaUI/) library implements those controls for UI.