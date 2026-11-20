# BHoM_Proto
Protocol Buffers representation of BHoM types

This repo is still in a prototype stage and might drastically change without prior notice, including, but not limited to, what types are included, how the files are structured, how properties are handled as well as their order and numbering.

## Contents

| Path | What it is |
|---|---|
| `bhom/*.proto` | Generated [Protocol Buffers](https://protobuf.dev/) definitions of the oM types. Compile these with `protoc` (or `Grpc.Tools`) for any language. |
| `bhom/options.proto` | Custom options linking each message, field and enum to the C# type, property or enum it represents, e.g. `option (.bhom.message_info).csharp_type = "BH.oM.Structure.Elements.Bar";`. Clients on other platforms can ignore them. |
| `bhom.protoset` | The `.proto` files compiled by `protoc` into a descriptor set. Used by the writer and reader in [Protobuf_Toolkit](https://github.com/BHoM/Protobuf_Toolkit), and read back on each generation so field numbers never change once published. Generated, never edited by hand: review changes in the `.proto` files. |
| `ProtoConfig.json` | Generation settings: which repositories are included, which assemblies share a file, and how particular interfaces are exchanged (`OpenUnionTypes`, `ClosedUnionTypes`, `SkippedUnionTypes`). |
| `GenerationReport.md` | Generated overview of the files, their imports, and how properties typed as interfaces were handled. |
| `.ci/Generation` | Console app that generates all of the above from the installed oM assemblies. |
| `.ci/unit-tests` | Tests that compile all `.proto` files with `protoc`, check `bhom.protoset` is up to date, and round trip a dummy object of every type through the protobuf writer and reader. |

Currently only the core object model ([BHoM/BHoM](https://github.com/BHoM/BHoM)) is included.

## How the files are organised

Each oM assembly gets its own file and package, e.g. `bhom/structure.proto` in package `bhom.structure` (`ProtoConfig.json` can place several assemblies in a shared file, but none do at the moment).

Interfaces and abstract classes become a union message with a `oneof value` over their implementations. Protobuf does not allow files to import each other in a loop, so an interface whose implementations live in other files is handled in one of these ways (see `GenerationReport.md` for every case):
- **`google.protobuf.Any`** for the interfaces in `OpenUnionTypes`: `IFragment` (so `Fragments` is `repeated google.protobuf.Any fragments = 3`), `IMaterialProperties` and `IElementM`. The field carries an `any_of` option naming the interface, e.g. `[(.bhom.field_info) = { property: "Fragments" any_of: "BH.oM.Base.IFragment" }]`. Any message whose C# type implements the interface can go in it. The BHoM reader ignores, with a warning, values that don't implement it, and values whose type is not in any loaded schema.
- **A closed union** for the interfaces in `ClosedUnionTypes`: `IGeometry` only covers the types in Geometry_oM. Implementations in other files (`RenderMesh` and `RenderPoint` in Graphics_oM) are not exchanged.
- Otherwise, a union defined in the file that uses it, or the union of the interface's own file with a `google.protobuf.Any other = 50000` slot for implementations in other files (e.g. `IBHoMObject`).

Field numbers never change once published. Properties that are not exchanged, or no longer exist, keep their numbers as `reserved`, recorded with `option (.bhom.message_info).reserved_member = "<property>=<number>";`.

All custom options of one kind are fields of one options message (`MessageInfo`, `FieldInfo`, `EnumInfo` in `bhom/options.proto`), attached with a single extension number, so new options never need a new number.

## Reading and writing objects

The writer and reader live in [Protobuf_Toolkit](https://github.com/BHoM/Protobuf_Toolkit):
- `obj.ToProtobuf()` and `bytes.FromProtobuf(type)`: plain messages. The reader needs to know the type, as with any protobuf message (in gRPC the service definition provides it).
- `obj.ToProtobufAny()` and `bytes.FromProtobufAny()`: the message wrapped in a `google.protobuf.Any`, whose type url names the message, so it can be read back without knowing the type.

When no schema file is given, the installed schema holding the type is used. Schemas are installed as `<RootPackage>.protoset` in `C:\ProgramData\BHoM\Resources\Protobuf`.

## Regenerating

Build [Protobuf_Toolkit](https://github.com/BHoM/Protobuf_Toolkit) so its assemblies are in `C:\ProgramData\BHoM\Assemblies`, then run:

```
dotnet run --project .ci/Generation/ProtoGeneration/ProtoGeneration/ProtoGeneration.csproj
```

This writes the `.proto` files, compiles them into `bhom.protoset`, and installs that in the BHoM resources folder. Add `--no-install` to only write the files in the repository.

Test with:

```
dotnet test .ci/unit-tests/BHoM_Proto_Tests/BHoM_Proto_Tests.sln
```
