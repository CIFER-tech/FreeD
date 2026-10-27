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

## 受信コンポーネント (FreeDReceiver)

Unity のカメラ (または任意のオブジェクト) に `FreeDReceiver` を付けるだけで、UDP で受信したFreeDが位置・向きに反映される。

1. パッケージを入れ、カメラに `FreeDReceiver` を追加する。
2. `port` を retroloc の「FreeD送信」のポート (既定 40000) に合わせる。
3. 再生すると、左上に受信状態・位置 (mm)・角度 (°) が出る。
4. 向きが逆なら `panSign` / `tiltSign` / `rollSign` を -1 にする。カメラ取り付けのずれは `positionOffset` / `rotationOffset` で合わせる。

座標は retroloc のワールド (X=右, Y=前, Z=上, m) から Unity へ `(x, y, z) → (x, z, y)` で変換する。

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
