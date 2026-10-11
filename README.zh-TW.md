<!-- 翻譯自 README.md @ docs/readme-template-v2。只翻 "For AI agents" 以上的章節，英文版更新時一起改。 -->

<div align="center">

<h1>Jurassic Park</h1>

<p><strong>選一個營地、築牆封起來、守住補給線，30 分鐘內把所有人送上直升機。</strong></p>

[![CI](https://img.shields.io/github/actions/workflow/status/zyx1121/jurassic-park/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/zyx1121/jurassic-park/actions)
[![Unity](https://img.shields.io/badge/Unity-6000.3%20LTS-000000?style=flat-square&logo=unity)](ProjectSettings/ProjectVersion.txt)
[![License](https://img.shields.io/github/license/zyx1121/jurassic-park?style=flat-square)](LICENSE)

[English](README.md) | **繁體中文** | [简体中文](README.zh-CN.md)

</div>

![M1 切片：一名倖存者離開營地入口前往樹林](docs/screenshots/m1-orthographic-2026-09-17.png)

Warcraft III 自訂地圖「侏羅紀公園」把基地防守變成合作求生的故事。你從眾多營地中選一個，封住入口，在壓力下發展經濟，最後拋下一切撤離。這個專案在 Unity 裡把它重做成獨立的 RTS，是 NYCU 3D 遊戲程式設計課 2026 秋季的期末專案。規則取自原版 6.3 地圖，並加上實體物流。

## 功能

- **RTS 操作**：選取倖存者與支援單位，右鍵下令。鏡頭角度固定。
- **一切靠搬運**：每份資源都要從來源、倉庫一路搬到工地。沒有共用的數字幫你付錢。
- **牆真的有用**：恐龍會繞過牆，或打破那面真正打開通路的牆。
- **朋友或電腦隊友**：每個座位可以是真人或 AI。玩家斷線時由 AI 接手。

## 快速開始

```sh
git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park
git lfs install && git lfs pull
unity open .        # 或用 Unity Hub 以 6000.3.24f1 開啟資料夾
```

打開 `Assets/Scenes/M1.unity` 按 Play。拖曳選取倖存者，再右鍵點地面、樹或倉庫。

## 使用方式

| 輸入 | 作用 |
|------|------|
| 左鍵、拖曳 | 選取 |
| 右鍵 | 下令：移動、砍樹、放進倉庫 |
| Shift | 排隊指令 |
| X | 停止 |
| WASD、螢幕邊緣 | 平移 |
| 滾輪 | 縮放 |

**多人**：按 Play 後，大廳有 **Play alone**、**Host** 與 **Join**。建置版也接受 `--host [port]`、`--join address[:port]` 或 `--offline`。加上 `-batchmode -nographics` 會以無畫面模式執行，每 5 秒記錄一行 `[Status]`。加入的玩家佔第一個空座位，離開後由電腦收回。

## 設定

| 需要 | 用途 |
|------|------|
| Unity 6000.3.24f1 | 專案本身 |
| Git LFS | sprite 與其他二進位素材 |
| Unity CLI(`unity`) | 測試、場景產生器與建置 |

遊戲數值放在 `Assets/Data/` 下的 ScriptableObject。場景由產生器指令建立：`unity run . -- -executeMethod JurassicPark.Editor.BuildM1Scene.Run`。

## 運作方式

- 5 個 assembly：`Simulation`(純 C#，不引用引擎)、`Presentation`、`Net`、`Editor` 與 `Tests.EditMode`。遊戲規則只在 `Simulation`。
- 只有 host 執行模擬。client 透過 Netcode for GameObjects 的具名訊息送指令、畫快照。
- 每個輸入，不論來自玩家、GUI 或 AI 座位，都經過 Command、Task、Plan 與 Action。
- 美術用數位化 sprite：真實素材、Blender 算圖、每公尺 64 px、統一的叢林調色盤。

| 文件 | 內容 |
|------|------|
| [docs/PLAN.md](docs/PLAN.md) | v1 設計：決策、指令流程、物流、地圖、對局流程、里程碑 |
| [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md) | 60 度鏡頭、綠色調色盤與 sprite 管線 |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) | 課程背景與日期 |

| 里程碑 | 期限 | 範圍 |
|--------|------|------|
| M0 起步與提案 | 10/18 | 專案、測試、CI、內容盤點、提案 |
| M1 指令與物流核心(完成) | 11/08 | 模擬核心、座位、指令路由、搬運、2 人連線 |
| M2 MVP | 11/15 | 完整一局：建造、恐龍、破牆、戰爭迷霧、AI 隊友、12 營地地圖、美術、GUI |
| M3 調整與期末 | 12/27 | 平衡、分數、音效、正式建置、報告 |

## 限制

- repo 裡沒有任何 Warcraft III 模型、貼圖、音效、UI 或地圖腳本。專案只研究規則、系統關係與數值。
- 目標平台：Apple Silicon 的 macOS 與 Windows。效能目標是 MacBook Air M2 上 60 fps。

## 其他

給 AI agent 的結構化資訊與開發說明在[英文版](README.md#for-ai-agents)。

## 授權

[MIT](LICENSE)

Clever girl.
