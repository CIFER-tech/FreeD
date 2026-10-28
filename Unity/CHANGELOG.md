# Changelog

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
