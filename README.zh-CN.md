<!-- 翻译自 README.md @ docs/readme-template-v2。只翻 "For AI agents" 以上的章节，英文版更新时一起改。 -->

<div align="center">

<h1>Jurassic Park</h1>

<p><strong>选一个营地、筑墙封起来、守住补给线，30 分钟内把所有人送上直升机。</strong></p>

[![CI](https://img.shields.io/github/actions/workflow/status/zyx1121/jurassic-park/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/zyx1121/jurassic-park/actions)
[![Unity](https://img.shields.io/badge/Unity-6000.3%20LTS-000000?style=flat-square&logo=unity)](ProjectSettings/ProjectVersion.txt)
[![License](https://img.shields.io/github/license/zyx1121/jurassic-park?style=flat-square)](LICENSE)

[English](README.md) | [繁體中文](README.zh-TW.md) | **简体中文**

</div>

![M1 切片：一名幸存者离开营地入口前往树林](docs/screenshots/m1-orthographic-2026-09-17.png)

Warcraft III 自定义地图「侏罗纪公园」把基地防守变成合作求生的故事。你从众多营地中选一个，封住入口，在压力下发展经济，最后抛下一切撤离。这个项目在 Unity 里把它重做成独立的 RTS，是 NYCU 3D 游戏程序设计课 2026 秋季的期末项目。规则取自原版 6.3 地图，并加上实体物流。

## 功能

- **RTS 操作**：选取幸存者与支援单位，右键下令。镜头角度固定。
- **一切靠搬运**：每份资源都要从来源、仓库一路搬到工地。没有共用的数字帮你付钱。
- **墙真的有用**：恐龙会绕过墙，或打破那面真正打开通路的墙。
- **朋友或电脑队友**：每个座位可以是真人或 AI。玩家掉线时由 AI 接手。

## 快速开始

```sh
git clone https://github.com/zyx1121/jurassic-park && cd jurassic-park
git lfs install && git lfs pull
unity open .        # 或用 Unity Hub 以 6000.3.24f1 打开文件夹
```

打开 `Assets/Scenes/M1.unity` 按 Play。拖动选取幸存者，再右键点地面、树或仓库。

## 使用方式

| 输入 | 作用 |
|------|------|
| 左键、拖动 | 选取 |
| 右键 | 下令：移动、砍树、放进仓库 |
| Shift | 排队指令 |
| X | 停止 |
| WASD、屏幕边缘 | 平移 |
| 滚轮 | 缩放 |

**多人**：按 Play 后，大厅有 **Play alone**、**Host** 与 **Join**。构建版也接受 `--host [port]`、`--join address[:port]` 或 `--offline`。加上 `-batchmode -nographics` 会以无界面模式运行，每 5 秒记录一行 `[Status]`。加入的玩家占第一个空座位，离开后由电脑收回。

## 配置

| 需要 | 用途 |
|------|------|
| Unity 6000.3.24f1 | 项目本身 |
| Git LFS | sprite 与其他二进制素材 |
| Unity CLI(`unity`) | 测试、场景生成器与构建 |

游戏数值放在 `Assets/Data/` 下的 ScriptableObject。场景由生成器命令创建：`unity run . -- -executeMethod JurassicPark.Editor.BuildM1Scene.Run`。

## 工作原理

- 5 个 assembly：`Simulation`(纯 C#，不引用引擎)、`Presentation`、`Net`、`Editor` 与 `Tests.EditMode`。游戏规则只在 `Simulation`。
- 只有 host 运行模拟。client 通过 Netcode for GameObjects 的具名消息发送指令、绘制快照。
- 每个输入，不论来自玩家、GUI 或 AI 座位，都经过 Command、Task、Plan 与 Action。
- 美术用数字化 sprite：真实素材、Blender 渲染、每米 64 px、统一的丛林调色板。

| 文档 | 内容 |
|------|------|
| [docs/PLAN.md](docs/PLAN.md) | v1 设计：决策、指令流程、物流、地图、对局流程、里程碑 |
| [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md) | 60 度镜头、绿色调色板与 sprite 管线 |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) | 课程背景与日期 |

| 里程碑 | 期限 | 范围 |
|--------|------|------|
| M0 起步与提案 | 10/18 | 项目、测试、CI、内容盘点、提案 |
| M1 指令与物流核心(完成) | 11/08 | 模拟核心、座位、指令路由、搬运、2 人联机 |
| M2 MVP | 11/15 | 完整一局：建造、恐龙、破墙、战争迷雾、AI 队友、12 营地地图、美术、GUI |
| M3 调整与期末 | 12/27 | 平衡、分数、音效、正式构建、报告 |

## 限制

- repo 里没有任何 Warcraft III 模型、贴图、音效、UI 或地图脚本。项目只研究规则、系统关系与数值。
- 目标平台：Apple Silicon 的 macOS 与 Windows。性能目标是 MacBook Air M2 上 60 fps。

## 其他

给 AI agent 的结构化信息与开发说明在[英文版](README.md#for-ai-agents)。

## 许可证

[MIT](LICENSE)

Clever girl.
