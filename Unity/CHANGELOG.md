# Changelog

## [1.1.1] - 2026-10-28

- `FreeD.Unity.asmdef` を追加 (パッケージ内スクリプトがコンパイルされるように)。`FreeD.dll` のメタ設定を修正。
- `FreeDReceiver` のコンパイルエラー (double→float) を修正。Unity 6 で実機受信を確認 (位置・向き・軸変換)。

## [1.1.0] - 2026-10-27

- `FreeDReceiver` を追加。UDP で受信したFreeDをカメラの位置・向きに反映する (retroloc座標→Unity座標変換、符号反転、手動オフセット付き)。

## [1.0.0] - 2026-10-23

- 初回リリース。`src/FreeD` の netstandard2.1 DLL を同梱した UPM パッケージ。
