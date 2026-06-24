using System.Text.Json.Serialization;

namespace Servo;

[JsonSerializable(typeof(List<string>))]
internal partial class ServoJsonContext : JsonSerializerContext;
