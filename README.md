# FreeD

FreeD (D1, 29 バイト) メッセージの符号化・復号ライブラリ (C#, netstandard2.1)。
普通の .NET アプリからも、Unity (2021.2+) のパッケージ (`src/FreeD.Unity/`) からも、同じ DLL (`CIFER.tech.FreeD.Core.dll`) で使える。Unity のパッケージには、ビルドした DLL を同梱する。

Unity からは Package Manager 経由でも導入できる ([`src/FreeD.Unity/`](src/FreeD.Unity) 参照、UPM パッケージ `tech.cifer.freed`)。
Unity のパッケージは、受信したパケットをためるために、リングバッファのパッケージ [`tech.cifer.ringbuffer`](https://github.com/CIFER-tech/RingBuffer) を使う (`Packages/manifest.json` に足す。手順は [`src/FreeD.Unity/README.md`](src/FreeD.Unity/README.md))。

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
- Unity では `FreeDManager`・`FreeDReceiverForUnity`・`FreeDCameraConsumer` が使っている ([`src/FreeD.Unity/`](src/FreeD.Unity) 参照)。

## Unity で原点を合わせる

`FreeDCameraConsumer` を付けたカメラの親のオブジェクトが、「FreeD 座標の原点が Unity のどこにあるか」になる。親の Position と Rotation の Y に、原点の位置と水平の向きを入れる (青い矢印 = FreeD の Y、赤い矢印 = X)。

- 吸収するのは位置 3 軸と水平の向きだけ。床は水平とみなすので、親の傾き (X・Z の回転) は 0 にする。

## 普通の .NET アプリで使う

ソースは `src/FreeD/` にある (netstandard2.1、名前空間は `CIFER.tech.FreeD.Core`)。符号化・復号・合成と、UDP の受信 (`FreeDReceiver`) を持つ。受信は RingBuffer ([`CIFER.tech.RingBuffer`](https://github.com/CIFER-tech/RingBuffer)) を使う。
`FreeDReceiver` は `IFreeDProvider` を実装する。`Port`・`DelaySec`・`ClaimedFields` を設定してから `Open()` する。開いている間は、これらを変えられない (`InvalidOperationException`)。変えるときは、`Close()` してから変えて、`Open()` し直す。

タグ (`v*`) を付けると、GitHub Actions が `.nupkg` を作って、GitHub の Releases に添付する。nuget.org には公開しない。取り出した `.nupkg` を置いたフォルダを、NuGet のローカルのソースにして使う (RingBuffer の `.nupkg` も同じ方法で取り出す)。

プロジェクトを直接参照することもできる。

```xml
<ProjectReference Include="..\FreeD\src\FreeD\FreeD.csproj" />
```

RingBuffer のリポジトリは、FreeD の隣に置く。別の場所に置くときは、`-p:RingBufferProject=パス` で変える。

## ビルドとテスト

```
dotnet build
dotnet test
```

テストは、手計算で作った期待バイト列との一致、往復の誤差(量子化幅以内)、不正入力の拒否、飽和などを確認する。
