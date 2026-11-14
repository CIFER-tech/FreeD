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

## 受信コンポーネント (FreeDManager・FreeDReceiver・FreeDTracker)

3 つのコンポーネントに分かれている。

- `FreeDManager`: シーンに 1 つ置く。受信の開始・停止と、カメラ ID ごとの合成をすべて管理する。シーンになければ、受信も反映も行われない。
- `FreeDReceiver`: ポート 1 つで受信するだけ。送信元のポートごとに 1 つ置く。
- `FreeDTracker`: カメラ ID を指定して値を受け取り、自分のオブジェクトの位置・向きに反映する。親のオブジェクトが retroloc の原点になる。

1. パッケージを入れ、空のオブジェクトに `FreeDManager` を追加する。
2. 空のオブジェクト (Manager の子でもよい) に `FreeDReceiver` を追加し、`Port` を retroloc の「FreeD送信」のポート (既定 40000) に合わせる。
3. 空のオブジェクトを 1 つ作り、retroloc の原点にあたる Unity 上の場所と向きに合わせる (Position と Rotation の Y)。Scene ビューの青い矢印 = retroloc の Y、赤い矢印 = X。床は水平とみなすので、Rotation の X・Z は 0 にする。
4. その子に Unity のカメラを置き、`FreeDTracker` を追加する。`Camera Id` を送信側のカメラ ID (retroloc は既定 1) に合わせる。
5. 再生すると、Receiver の Inspector にポートごとの受信状態が、Manager の Inspector にカメラ ID ごとの受信状態が、Tracker の Inspector に位置 (mm)・角度 (°) が出る。
6. 向きが逆なら、`Rotation Scale` の該当する軸 (X=パン, Y=チルト, Z=ロール) を -1 にする。位置が逆なら、`Position Scale` の該当する軸 (X=右, Y=前, Z=上) を -1 にする。カメラ取り付けのずれは `Position Offset` / `Rotation Offset` で合わせる (どちらも Unity の座標・向きで指定。カメラのローカル軸: 右・上・前、回転は Transform の Rotation と同じ)。

座標は retroloc のワールド (X=右, Y=前, Z=上, m) から Unity へ `(x, y, z) → (x, z, y)` で変換する。

### 設計 (MVP)

| 役割 | コンポーネント | 持つもの |
| --- | --- | --- |
| Model | `FreeDReceiver` | ポートで受信した値と、その受信状態。ほかを知らない |
| View | `FreeDTracker`、`FreeDLens` | 受け取った値の表示 (カメラの位置・向き・レンズ)。ほかを知らない |
| Presenter | `FreeDManager` | Model と View の寿命、毎フレームの取り出し・合成・反映 |

Model と View は、Unity の `Awake`・`OnEnable`・`Update` などを使わない。動かすのは Presenter だけで、Model と View は互いを参照しない。

### 複数のポートを合成する (姿勢 + レンズ)

姿勢は retroloc、ズームとフォーカスは別のシステム、のように別々のポートで届くときは、`FreeDReceiver` をポートごとに置き、使う項目を選ぶ。

1. `FreeDReceiver` を 2 つ置く。1 つ目: `Port` 40000、`Fields` で `Position` と `Rotation` だけ選ぶ。2 つ目: `Port` 41000、`Fields` で `Zoom` と `Focus` だけ選ぶ。
2. 同じ項目は 1 つの Receiver だけが使える。重なっていると、Manager がエラーを出して、受信を始めない。
3. 項目ごとに、その Receiver で最後に届いた値を使う。途切れても値は保たれ、Receiver の状態で、そのポートが途切れ (`Is Receiving` が false) になる。
4. 映像とのずれは、早く届く側の Receiver の `Delay Ms` で合わせる (その分だけ反映を遅らせる)。
5. 合成はカメラ ID ごとに行う。姿勢とレンズを 1 台のカメラにまとめるには、どちらのシステムも同じカメラ ID で送る。
6. ズームとフォーカスの生の値 (24bit) は `FreeDTracker` の `zoomRaw` / `focusRaw` に出る。カメラに反映するには、Tracker と同じオブジェクトに `FreeDLens` を付け、AnimationCurve に対応を入れる (横軸 = 生の値、縦軸 = 焦点距離 mm / ピント距離 m)。焦点距離は Physical Camera に入る。ピント距離は Unity 2022.2 以降で `Camera.focusDistance` にも入り、`FreeDLens.focusDistanceM` からも読める。

2.0.0 より前のシーン (カメラに `FreeDReceiver` を付けていたもの) は、上の手順で組み直す。

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
