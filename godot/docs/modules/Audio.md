# 音频

代码目录：播放调用可以在各模块，总线和资源约定只写在本文件。资源放 `assets/audio/`。

原项目：OpenAL，支持 Vorbis、MP3、FLAC，另有环境声、材质脚步、音乐。

## 必须

- 用 Godot 音频总线区分音乐、环境、界面、动作。
- 脚步声按踩到的方块材质切换。
- 源文件用 Ogg 或 Wav。仓库里的 FLAC 在导入前转换，不要在运行时嵌 FLAC 解码器。
- 音乐和音效的开关、音量接到 [Settings.md](Settings.md)。

## 禁止

- 把 Silk.NET.OpenAL 或 NAudio 引用进本工程。
- 用循环调用 `AudioStreamPlayer.Play` 从 C# 每帧播一个采样来假装音源。

## 完成标准

挖掘、脚步、受伤、音乐、雨声都能听见，音量设置会保存。没有 FLAC 运行时依赖。
