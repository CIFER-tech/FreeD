# FreeD (Unity Package)

FreeD (D1, 29 バイト) メッセージの符号化・復号ライブラリ。[`src/FreeD`](../src/FreeD) をビルドした
netstandard2.1 の DLL を同梱している (Unity 2021.2+ 向け)。

## インストール

Package Manager → `+` → `Add package from git URL...` で以下を指定する。

```
https://github.com/<owner>/FreeD.git?path=Unity
```

特定バージョンに固定する場合は `#v1.0.0` のようにタグを付ける。

## 使い方

```csharp
using FreeD;

var packet = new FreeDPacket
{
    CameraId = 1,
    PanDegrees = 90, TiltDegrees = -45, RollDegrees = 0,
    XMeters = 1.0, YMeters = -0.5, ZMeters = 2.0,
};

var buffer = new byte[FreeDCodec.PacketLength];
FreeDCodec.Encode(packet, buffer);

if (FreeDCodec.TryDecode(received, out FreeDPacket p)) { /* ... */ }
```

詳しいメッセージ配置・仕様の注意点はリポジトリ直下の [README](../README.md) を参照。

## DLL の更新について

このフォルダの `Runtime/FreeD.dll` は `src/FreeD` のビルド出力をそのまま同梱したもの。
`src/FreeD/**` を変更して `main` にpushすると、
[`.github/workflows/sync-unity-dll.yml`](../.github/workflows/sync-unity-dll.yml) が自動的に
Release ビルドして `Runtime/FreeD.dll` / `Runtime/FreeD.pdb` を差分コミットする。

手元で確認したい場合は以下を実行する。

```
dotnet build src/FreeD/FreeD.csproj -c Release
cp src/FreeD/bin/Release/netstandard2.1/FreeD.dll Unity/Runtime/FreeD.dll
cp src/FreeD/bin/Release/netstandard2.1/FreeD.pdb Unity/Runtime/FreeD.pdb
```
