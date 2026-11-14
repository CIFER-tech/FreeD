# FreeD

FreeD (D1, 29 バイト) メッセージの符号化・復号ライブラリ (C#, netstandard2.1)。
普通の .NET アプリからも、Unity (2021.2+) のパッケージ (`Unity/`) からも、DLL を作らずに、同じソースで使える。

Unity からは Package Manager 経由でも導入できる ([`Unity/`](Unity) 参照、UPM パッケージ `tech.cifer.freed`)。
Unity のパッケージは、受信したパケットをためるために、リングバッファのパッケージ [`tech.cifer.ringbuffer`](https://github.com/CIFER-tech/RingBuffer) を使う (`Packages/manifest.json` に足す。手順は [`Unity/README.md`](Unity/README.md))。

## 使い方

```csharp
using CIFER.tech.FreeD.Core;

var packet = new FreeDPacket
{
    CameraId = 1,
    PanDegrees = 90, TiltDegrees = -45, RollDegrees = 0,   // 度
    XMeters = 1.0, YMeters = -0.5, ZMeters = 2.0,          // メートル
};

var buffer = new byte[FreeDCodec.PacketLength];
FreeDCodec.Encode(packet, buffer);                  // 29 バイト。24bit の範囲を超える値は飽和
// udp.Send(buffer, FreeDCodec.PacketLength, ...);

if (FreeDCodec.TryDecode(received, out FreeDPacket p)) { /* 長さ・種別・チェックサムが正しいときだけ true */ }
```

## メッセージ配置

```
[0]      0xD1
[1]      カメラ ID
[2..4]   パン      24bit 符号付き 1/32768 度
[5..7]   チルト    同上
[8..10]  ロール    同上
[11..13] X         24bit 符号付き 1/64 mm
[14..16] Y         同上
[17..19] Z         同上
[20..22] ズーム    24bit 符号なし (生値)
[23..25] フォーカス 24bit 符号なし (生値)
[26..27] ユーザーデータ
[28]     チェックサム = (0x40 − Σ[0..27]) mod 256
```

**公式の仕様書原本は未確認。** 受信機ごとの軸の向き・角度の符号・ズーム/フォーカスの扱いは実機で確認すること
(このライブラリは値をそのまま載せるだけで、符号の反転や座標系の変換は呼び出し側の仕事)。

## 複数のストリームを合成する

姿勢とレンズ (ズーム・フォーカス) のように、別々に届く FreeD を `FreeDMerger.Merge` で 1 つの `FreeDPacket` にまとめる。

```csharp
// 姿勢は posePacket から、ズームとフォーカスは lensPacket から取った新しいパケットが返る
FreeDPacket merged = FreeDMerger.Merge(
    posePacket, FreeDField.Rotation | FreeDField.Position,
    lensPacket, FreeDField.Zoom | FreeDField.Focus);
```

- 項目が重なったときは、2 つ目のパケットの値を使う。
- 状態は持たない。最後に届いた値を保つこと、遅延、途切れの扱いは呼び出し側で行う。
- Unity では `FreeDManager`・`FreeDReceiverForUnity`・`FreeDTracker` が使っている ([`Unity/`](Unity) 参照)。

## Unity で原点を合わせる

`FreeDTracker` を付けたカメラの親のオブジェクトが、「FreeD 座標の原点が Unity のどこにあるか」になる。親の Position と Rotation の Y に、原点の位置と水平の向きを入れる (青い矢印 = FreeD の Y、赤い矢印 = X)。

- 吸収するのは位置 3 軸と水平の向きだけ。床は水平とみなすので、親の傾き (X・Z の回転) は 0 にする。

## 普通の .NET アプリで使う

ソースは `Unity/Runtime/Core/` にある。DLL は作らず、使うプロジェクトの `.csproj` から、リポジトリ直下の `FreeD.Core.props` を取り込む。

```xml
<Import Project="..\FreeD\FreeD.Core.props" />

<ItemGroup>
  <!-- FreeDReceiver が使う。RingBuffer のリポジトリを、FreeD の隣に置く -->
  <ProjectReference Include="..\RingBuffer\src\RingBuffer\RingBuffer.csproj" />
</ItemGroup>
```

- ソースは netstandard2.1 以降で、`#nullable enable` で書いてある。
- 名前空間は `CIFER.tech.FreeD.Core`。

## ビルドとテスト

```
dotnet build
dotnet test
```

テストは、手計算で作った期待バイト列との一致、往復の誤差(量子化幅以内)、不正入力の拒否、飽和などを確認する。
