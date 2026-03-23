![GitHub release (latest by date)](https://img.shields.io/github/v/release/c-colloid/VRM10SpringBoneEditor?label=release)
![GitHub release (by tag)](https://img.shields.io/github/downloads/c-colloid/VRM10SpringBoneEditor/latest/total)
![GitHub all releases](https://img.shields.io/github/downloads/c-colloid/VRM10SpringBoneEditor/total?label=total%20downloads)
![GitHub issues](https://img.shields.io/github/issues/c-colloid/VRM10SpringBoneEditor)
![Github stars](https://img.shields.io/github/stars/c-colloid/VRM10SpringBoneEditor)

# VRM10SpringBoneEditor
 VRM10SpringBonesの設定を簡易化する拡張機能。
 
 Extension to simplify the configuration of VRM10SpringBones.

# 使い方　How to use
 1. アバター内の任意のオブジェクトに`VRM10SpringBoneEx`をつける

 2. `RootBone`にボーンの根元となるオブジェクトをつける

 3. 枝分かれがある場合：必要な枝をチェックボックスで選び`Generate Selected`ボタンを押す

 4. `VRM0.x`の設定と同じようにパラメータを設定

 5. ボーン毎に設定を変えたい場合は`C`ボタンを押してカーブウィンドウを使う

# 既知の問題　Bugs
- 子ボーンが枝分かれしている場合、全ての子ボーンが1房と認識され望む動きにならない

# 実装予定　TODO
- ボーンの枝分かれに対応

- 除外ボーン設定の実装

# 開発環境
- VRM1.0 0.125.0~
