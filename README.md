# FreeD

FreeD (D1, 29 バイト) メッセージの符号化・復号ライブラリ (C#, netstandard2.1)。
.NET 8 のアプリからも、Unity (2021.2+) の `Assets/Plugins/` に置いた DLL からも使える。
[retroloc](../retroloc) から切り出した共有ライブラリで、他のプロジェクトからも参照する想定。

Unity からは Package Manager 経由でも導入できる ([`Unity/`](Unity) 参照、UPM パッケージ `tech.cifer.freed`)。

## 使い方

```csharp
using FreeD;

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

配置は Vizrt Tracking Hub の設定例と OSS の C# 実装の 2 系統で一致を確認した。
**公式の仕様書原本は未確認。** 受信機ごとの軸の向き・角度の符号・ズーム/フォーカスの扱いは実機で確認すること
(このライブラリは値をそのまま載せるだけで、符号の反転や座標系の変換は呼び出し側の仕事)。

## Unity で原点を合わせる

`FreeDReceiver` を付けたカメラを、`FreeDOrigin` を付けた空のオブジェクトの子にする。`FreeDOrigin` の位置と水平の向きが「retroloc の原点が Unity のどこにあるか」になる。手で置いてもよいし、実行中に画面左上のボタンで合わせてもよい。

- 1 か所: カメラを目印 A の位置に置き、目印の前 (青い矢印) に向けて「1 か所で合わせる」。
- 2 か所: 目印 A で「A を記録」、B に移して「B を記録して合わせる」。A→B の線から向きを求める (向きの精度が高い)。2 点は 0.3 m 以上離す。
- 結果は `freed-origin.json` (エディタではプロジェクトのフォルダ、ビルドでは exe のフォルダ) に保存し、次の起動時に読む。ファイルがあればシーン上の置き場所より優先する。「合わせを消す」でファイルを消してシーン上の置き場所に戻す。
- 吸収するのは位置 3 軸と水平の向きだけ。床は水平とみなす。

## ビルドとテスト

```
dotnet build
dotnet test
```

テストは、手計算で作った期待バイト列との一致、往復の誤差(量子化幅以内)、不正入力の拒否、飽和などを確認する。

## 使っているリポジトリ

- retroloc: `src/Retroloc.Core` が `..\..\..\FreeD\src\FreeD\FreeD.csproj` を参照する(同じ親フォルダに両方をクローンしておく)。
  姿勢 (`Pose`) との対応付け `FreeDMapping` は retroloc 側にある。
