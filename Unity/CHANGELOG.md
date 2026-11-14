# Changelog

## [1.3.1] - 2026-11-14

- `FreeDReceiver` のストリームで、使う項目を 4 つのチェックボックスから、複数選べるドロップダウン (`fields`) に変更。1.3.0 までのシーンのチェックは自動で移る。
- 状態の欄を、編集できない表示だけの欄にした (Editor 用のアセンブリ `FreeD.Unity.Editor` を追加)。`FreeDReceiver`・各ストリーム・`FreeDLens` の状態の欄は、枠で囲んでまとめた (見出しは付けない)。
- `panSign` / `tiltSign` / `rollSign` を、ストリームごとの `rotationScale` (Vector3。X=パン, Y=チルト, Z=ロール) に変更。符号ではなく倍率としてかけているので名前も改めた。1.3.0 までのシーンの値は、回転を使うストリームへ自動で移る。
- `FreeDOrigin` の `originMarker` を廃止。原点は `FreeDOrigin` 自身の Position と Rotation の Y で決める。
- `FreeDReceiver` の `pan`/`tilt`/`roll` と `xMm`/`yMm`/`zMm` を、`panTiltRollDeg` と `positionMm` (どちらも Vector3) にまとめた。`zoomRaw` を `hasZoom` の下、`focusRaw` を `hasFocus` の下に並べた。

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
