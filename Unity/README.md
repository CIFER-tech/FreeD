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
2. `Streams` の 1 本目の `Port` を retroloc の「FreeD送信」のポート (既定 40000) に合わせる。
3. 再生すると、左上に受信状態・位置 (mm)・角度 (°) が出る。
4. 向きが逆なら `panSign` / `tiltSign` / `rollSign` を -1 にする。カメラ取り付けのずれは `positionOffset` / `rotationOffset` で合わせる (どちらも Unity の座標・向きで指定。カメラのローカル軸: 右・上・前、回転は Transform の Rotation と同じ)。

座標は retroloc のワールド (X=右, Y=前, Z=上, m) から Unity へ `(x, y, z) → (x, z, y)` で変換する。

### 複数のストリームを合成する (姿勢 + レンズ)

姿勢は retroloc、ズームとフォーカスは別のシステム、のように別々の FreeD ストリームで届くときは、`Streams` にストリームを足して、ポートごとに使う項目を選ぶ。

1. `Streams` を 2 本にする。1 本目: `Port` 40000、`Rotation` と `Position` だけチェック。2 本目: `Port` 41000、`Zoom` と `Focus` だけチェック。
2. 同じ項目は 1 つのストリームだけが使える。チェックすると、他のストリームのチェックが外れる。
3. 項目ごとに、そのストリームで最後に届いた値を使う。途切れても値は保たれ、左上の表示でそのストリームが「途切れ」になる。
4. 映像とのずれは、早く届く側のストリームの `Delay Ms` で合わせる (その分だけ反映を遅らせる)。
5. ズームとフォーカスの生の値 (24bit) は `zoomRaw` / `focusRaw` に出る。カメラに反映するには、同じオブジェクトに `FreeDLens` を付け、AnimationCurve に対応を入れる (横軸 = 生の値、縦軸 = 焦点距離 mm / ピント距離 m)。焦点距離は Physical Camera に入る。ピント距離は Unity 2022.2 以降で `Camera.focusDistance` にも入り、`FreeDLens.focusDistanceM` からも読める。

1.2 までのシーンの `port` / `cameraId` は、読み込むと `Streams` の 1 本目に移る。

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
