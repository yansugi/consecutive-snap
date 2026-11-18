# ConsecutiveSnap

指定した画面範囲を、「撮影 → 操作（キー／クリック／待機）→ 撮影 → …」の順で指定回数だけ繰り返し、
連番の PNG として保存する Windows アプリです。ページをめくりながら同じ範囲を撮り続ける用途向けです。

- 要件: [docs/requirements.md](docs/requirements.md)
- 設計: [docs/technical-design.md](docs/technical-design.md)

## 使い方
1. 「範囲を指定...」で撮影範囲をドラッグして指定する
2. 「操作シーケンス」に、撮影の合間に実行する操作（キー / クリック / マウスホイール / 待機）を上から順に登録する
   （マウスホイールは上下方向とノッチ数を指定します。ホイールはカーソル下のウィンドウに届くため、位置の指定もできます）
3. 繰り返し回数・開始前の待機・保存先（親フォルダとフォルダ名）を入力する
4. 「開始」を押し、開始前の待機の間に対象アプリへフォーカスを移す
5. 止めたいときは **Pause/Break キー**（ノート PC では Fn との組み合わせの場合があります）

保存先は `親フォルダ\フォルダ名\` を新規作成し、中に `01.png` … のように連番で保存します
（桁数は繰り返し回数に合わせた 0 埋め。同名フォルダがあれば `フォルダ名_2` のように別名で作成します）。

## ビルド・テスト・発行
```
dotnet build
dotnet test
dotnet publish src/ConsecutiveSnap.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/ConsecutiveSnap-portable
```
製品プロジェクト（Core / App）は NuGet パッケージを一切参照しません。テスト専用の xUnit のみ `tests/` で参照します。

## 既知の制約
- フルスクリーン排他モードや DRM 保護された画面は、黒く写ることがあります
- 管理者権限で動いているアプリには、キー・クリックを送れません
