# Changelog

## [2.0.0] - 2026-11-14

互換性のない変更。1.3.0 までのシーンは組み直しが必要 (手順は README)。

- `FreeDReceiver` を、受信・合成・反映をすべて持つ形から、3 つのコンポーネントに分けた (MVP)。
  - `FreeDManager` (新規、Presenter): シーンに 1 つ置く。Receiver と Tracker の寿命と毎フレームの処理をすべて持つ。各 Receiver のパケットを取り出し、カメラ ID ごとに合成して Tracker に反映する。シーンになければ、受信も反映も行われない。Receiver と Tracker は開始時にシーンから探す (あとから置いたものは、再生を始め直すまで対象にならない)。
  - `FreeDReceiverForUnity` (Model): ポート 1 つで受信して、復号したパケットをためるだけ。受信そのものは `FreeDReceiver` が行う。ポートごとに置き、使う項目 (`field`) と遅延 (`delaySec`、秒) を持つ。`streams` は廃止。同じ項目を 2 つの Receiver が担当していると、Manager がエラーを出して、反映を行わない。
  - `FreeDTracker` (新規、View): `cameraId` を指定して値を受け取り、自分のオブジェクトの位置・向き・レンズに反映する。`rotationScale` (旧 `panSign` / `tiltSign` / `rollSign`。符号ではなく倍率としてかけるので改名)・`positionScale` (新規。位置にかける倍率)・`positionOffset` / `rotationOffset` を持つ。ズームとフォーカスは、`Camera` の欄に割り当てたカメラへ反映する (欄が空だと反映しない)。設定は private な `SerializeField` と public プロパティで持つ。
  - Receiver と Tracker は、Unity の `Awake`・`OnEnable`・`Update` などを使わない。
- 同梱する DLL を、`FreeD.dll` から `CIFER.tech.FreeD.Core.dll` に変えた。符号化・復号・合成の型 (`FreeDCodec`・`FreeDMerger`・`FreeDPacket`・`FreeDField`) だけを持ち、名前空間は `FreeD` から `CIFER.tech.FreeD.Core` に変わる。受信とコンポーネントは `Runtime/` のソースで、アセンブリ `CIFER.tech.FreeD.Unity` (名前空間 `CIFER.tech.FreeD.Unity`) になる。
- 受信したパケットは、固定サイズのリングバッファにためる。Manager が取りに来ない間にあふれたら、古いものを捨てる。
- 受信したパケットをためる待ち行列を、自前の実装から、パッケージ `tech.cifer.ringbuffer` の `RingBuffer<T>` に置き換えた。FreeD を使うプロジェクトの `Packages/manifest.json` に、このパッケージを足す必要がある。
- `FreeDOrigin` を廃止。`FreeDTracker` の親のオブジェクトが原点になる。親の Position と Rotation の Y で、原点の位置と向きを決める。
- `FreeDLens` を廃止し、ズーム・フォーカスの AnimationCurve と焦点距離・ピント距離の反映を `FreeDTracker` に統合した。
- 使う項目を、4 つのチェックボックスから、複数選べるドロップダウン (`FreeDField`) に変更。並びは Position・Rotation・Zoom・Focus。
- 状態の表示を廃止。Inspector の状態の欄 (受信中・pkt/s・受信間隔・角度・位置・ズームとフォーカスの値など) と、再生中の左上の表示 (OnGUI) を消した。

## [1.3.0] - 2026-10-30

- 複数の FreeD ストリームを 1 つに合成できるようにした。たとえば姿勢を retroloc から、ズームとフォーカスをレンズエンコーダから、別々のポートで受け取れる。
  - ライブラリに `FreeDMerger` を追加。ストリームごとに回転・位置・ズーム・フォーカスのどれを使うかを選び、項目ごとに最後に届いた値を使う。ストリームごとの遅延 (ms) で届く時刻のずれをそろえられる。
  - `FreeDReceiver` の `port` / `cameraId` をストリームの一覧 (`streams`) に変更。1.2 までのシーンの値は、一覧の 1 本目へ自動で移る。
  - `FreeDLens` を追加。ズーム・フォーカスの生の値を、AnimationCurve で焦点距離 (mm) とピント距離 (m) に直してカメラに反映する。

## [1.2.0] - 2026-10-29

- `FreeDReceiver` の `positionOffset` / `rotationOffset` を Unity 座標 (カメラのローカル軸: X=右, Y=上, Z=前。回転は Transform の Rotation と同じ向き) で指定するように変更。以前は FreeD 座標 (X右 Y前 Z上) だったので、使っていた値は入れ直しが必要。

## [1.1.2] - 2026-10-28

- `FreeDReceiver` に受信間隔(平均・最大)の表示を追加。

## [1.1.1] - 2026-10-28

- `FreeD.Unity.asmdef` を追加 (パッケージ内スクリプトがコンパイルされるように)。`FreeD.dll` のメタ設定を修正。
- `FreeDReceiver` のコンパイルエラー (double→float) を修正。Unity 6 で実機受信を確認 (位置・向き・軸変換)。

## [1.1.0] - 2026-10-27

- `FreeDReceiver` を追加。UDP で受信したFreeDをカメラの位置・向きに反映する (retroloc座標→Unity座標変換、符号反転、手動オフセット付き)。

## [1.0.0] - 2026-10-23

- 初回リリース。`src/FreeD` の netstandard2.1 DLL を同梱した UPM パッケージ。
