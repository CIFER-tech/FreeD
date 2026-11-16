# FreeD (Unity Package)

FreeD (D1, 29 バイト) メッセージの符号化・復号と、Unity での受信・反映のためのパッケージ (Unity 2021.2+ 向け)。

## インストール

Package Manager → `+` → `Add package from git URL...` で以下を指定する。

```
https://github.com/<owner>/FreeD.git?path=src/FreeD.Unity
```

特定バージョンに固定する場合は `#v1.0.0` のようにタグを付ける。

## 使い方

```csharp
using CIFER.tech.FreeD.Core;

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

詳しいメッセージ配置・仕様の注意点はリポジトリ直下の [README](../../README.md) を参照。

## 必要なパッケージ

受信したパケットをためるために、リングバッファのパッケージ [`tech.cifer.ringbuffer`](https://github.com/CIFER-tech/RingBuffer) (4.0.1 以降。ビルドした DLL を同梱している) を使う。`Packages/manifest.json` の `dependencies` に足しておく (FreeD と同じ置き方なら、次のとおり)。

```json
"tech.cifer.ringbuffer": "file:../../../RingBuffer/src/RingBuffer.Unity"
```

## 受信コンポーネント (FreeDManager・FreeDReceiverForUnity・FreeDTracker)

3 つのコンポーネントに分かれている。

- `FreeDManager`: アプリの間、1 つだけ存在する (`FreeDManager.Instance`)。シーンに置かなくても、`FreeDReceiverForUnity` や `FreeDTracker` が有効になったときに、自動で作られる。シーンを切り替えても残る。受信の開始・停止と、カメラ ID ごとの合成をすべて管理する。有効になっていない `FreeDReceiverForUnity` は、登録されない (控えの Receiver は無効にして置く)。
- `FreeDReceiverForUnity`: ポート 1 つで受信するだけ (受信そのものは `FreeDReceiver` が行う)。送信元のポートごとに 1 つ置く。
- `FreeDTracker`: カメラ ID を指定して値を受け取り、自分のオブジェクトの位置・向きに反映する。親のオブジェクトが FreeD 座標の原点になる。

1. パッケージを入れる。`FreeDManager` は、置かなくてよい。置くときは、ルートの GameObject に置く (子に置くと、シーンの切り替えで壊れる)。
2. 空のオブジェクト (Manager の子でもよい) に `FreeDReceiverForUnity` を追加し、`Port` を送信側のポート (既定 40000) に合わせる。
3. 空のオブジェクトを 1 つ作り、FreeD 座標の原点にあたる Unity 上の場所と向きに合わせる (Position と Rotation の Y)。Scene ビューの青い矢印 = FreeD の Y、赤い矢印 = X。床は水平とみなすので、Rotation の X・Z は 0 にする。
4. その子に Unity のカメラを置き、`FreeDTracker` を追加する。`Camera Id` を送信側のカメラ ID に合わせ、`Camera` の欄にそのカメラを割り当てる。`Camera` の欄が空だと、ズームとフォーカスは反映されない (位置と向きは反映される)。
5. 再生すると、カメラが受信した位置・向きに動く。向きが逆なら、`Rotation Scale` の該当する軸 (X=パン, Y=チルト, Z=ロール) を -1 にする。位置が逆なら、`Position Scale` の該当する軸 (X=右, Y=前, Z=上) を -1 にする。カメラ取り付けのずれは `Position Offset` / `Rotation Offset` で合わせる (どちらも Unity の座標・向きで指定。カメラのローカル軸: 右・上・前、回転は Transform の Rotation と同じ)。

座標は FreeD 座標 (X=右, Y=前, Z=上, m) から Unity へ `(x, y, z) → (x, z, y)` で変換する。

### 設計 (MVP)

| 役割 | コンポーネント | 持つもの |
| --- | --- | --- |
| Model | `FreeDReceiverForUnity` | ポートで受信した値。ほかを知らない |
| View | `FreeDTracker` | 受け取った値の表示 (カメラの位置・向き・レンズ)。ほかを知らない |
| Presenter | `FreeDManager` | Model と View の寿命、毎フレームの取り出し・合成・反映 |

Model と View は、`OnEnable` と `OnDisable` で、`FreeDManager` に登録・解除するだけで、`Update` などは使わない。動かすのは Presenter だけで、Model と View は互いを参照しない。

### 複数のポートを合成する (姿勢 + レンズ)

姿勢とレンズ (ズーム・フォーカス) が別々のポートで届くときは、`FreeDReceiverForUnity` をポートごとに置き、使う項目を選ぶ。

1. `FreeDReceiverForUnity` を 2 つ置く。1 つ目: `Port` 40000、`Claimed Fields` で `Position` と `Rotation` だけ選ぶ。2 つ目: `Port` 41000、`Claimed Fields` で `Zoom` と `Focus` だけ選ぶ。
2. 同じ項目は 1 つの Receiver だけが使える。重なっていると、Manager がエラーを出して、反映を行わない。
3. 項目ごとに、その Receiver で最後に届いた値を使う。途切れても値は保たれる。
4. 映像とのずれは、早く届く側の Receiver の `Delay Sec` (秒) で合わせる (その分だけ反映を遅らせる)。
5. 合成はカメラ ID ごとに行う。姿勢とレンズを 1 台のカメラにまとめるには、どちらの送信元も同じカメラ ID で送る。
6. ズームとフォーカスの生の値 (24bit) をカメラに反映するには、`FreeDTracker` の AnimationCurve に対応を入れる (横軸 = 生の値、縦軸 = 焦点距離 mm / ピント距離 m)。初期値は、生の値の全範囲 (0 から 16777215) を 0 から 1 に写す直線なので、実際のレンズに合わせて入れ直す。キーのない曲線は反映しない。焦点距離は Physical Camera に入る。ピント距離は Unity 2022.2 以降で `Camera.focusDistance` に入る。

2.0.0 より前のシーン (カメラに `FreeDReceiver` を付けていたもの) は、上の手順で組み直す。

### 自作の Receiver や Tracker を差し込む

`IFreeDReceiver` (受信) と `IFreeDTracker` (反映) を実装して、`FreeDManager.Instance` に登録すると、`FreeDReceiverForUnity` や `FreeDTracker` の代わりに使える。たとえば、UDP ではなく、録画を再生する Receiver を作れる。

```csharp
public sealed class RecordedReceiver : MonoBehaviour, IFreeDReceiver
{
    public FreeDField ClaimedFields { get; set; } = FreeDField.All;
    public float DelaySec { get; set; }

    public void Open() { /* 再生を始める */ }
    public void Close() { /* 再生を止める */ }
    public bool TryTakeNext(out FreeDReceivedPacket received) { /* 次のパケットを返す */ }

    private FreeDManager manager;

    private void OnEnable()
    {
        manager = FreeDManager.Instance;
        manager.AddReceiver(this);   // 登録すると、FreeDManager が Open を呼ぶ
    }

    private void OnDisable()
    {
        // 解除すると、FreeDManager が Close を呼ぶ。Instance は、なければ作ってしまうので、登録した相手を覚えておいて使う。
        if (manager != null)
        {
            manager.RemoveReceiver(this);
            manager = null;
        }
    }
}
```

- `FreeDManager.Instance` は、なければ作り、`Awake` と `OnEnable` まで済ませてから返す。シーンを閉じるときや終了するときに、作ってしまうので、`OnDisable` と `OnDestroy` では呼ばない。登録した `FreeDManager` を覚えておいて、解除に使う。
- singleton の仕組みは、`public` な基底クラス `SingletonBehaviour<T>` にあり、`FreeDManager` が使っている。
- `ClaimedFields` と `DelaySec` は、`Open` の時点の値で動く。開いている間に変えると、`InvalidOperationException` になる (`FreeDReceiver` の動作)。変えるときは、`Close()`、変更、`Open()` の順に行う。
- `FreeDReceiverForUnity` のインスペクタの値を、再生中に変えたときは、コンポーネントを無効にして、有効にし直すと反映される。
- ポート番号は、`IFreeDReceiver` に含めない。ポートを持つのは、`FreeDReceiver` の `Port` だけ。

## ソースの置き場所について

符号化・復号・合成と受信 (`FreeDReceiver`) のソースは [`src/FreeD`](../FreeD) にあり、ビルドした `Runtime/CIFER.tech.FreeD.Core.dll` を同梱している。`main` に `src/FreeD` の変更が入ると、GitHub Actions が DLL を作り直して、この場所へコミットする。
コンポーネントのソースは `Runtime/` にあり、アセンブリ `CIFER.tech.FreeD.Unity` が、FreeD と RingBuffer の 2 つの DLL を参照する。

```
dotnet test
```
