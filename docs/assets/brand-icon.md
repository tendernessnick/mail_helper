# MailHelper 图标资源

图标采用深松绿圆角底、奶白折叠信封与薄荷纸签。使用手工矢量源，没有外部图片或字体依赖；小尺寸保留同一轮廓，不加入难辨识的文字与装饰角标。

![MailHelper](logo.png)

## 单一源与导出

- `mailhelper-mark.svg`：可编辑的单一矢量源。
- `src/MailHelper.App/Assets/app.ico`：16/20/24/32/40/48/64/128/256px 九帧 Windows 图标，供 EXE、窗口标题、任务栏与安装器使用。
- `src/MailHelper.App/Assets/brand-mark.png`：256px，供 WPF 界面品牌区使用。
- `src/MailHelper.App.Avalonia/Assets/icon-source.png`：1024px，供 Avalonia 品牌、窗口、托盘与 macOS iconset 使用。
- `docs/assets/logo.png`：256px 文档展示图。

托盘以同一资产作为底图，未读时才叠加高对比数字角标；超过 99 以 `99+` 表示，完整未读数仍显示在提示中。

## 重建

正常构建不需要 Node.js 或图像库。仅重新生成图标时需要 Node.js 和 sharp：

```powershell
npm install --no-save --prefix artifacts/icon-tools sharp
node tools/make-icon.mjs
```

已有 sharp 时可指定 `MAILHELPER_ICON_SHARP_PATH`，或运行 `tools/make-icon.ps1 -SharpPath <sharp目录>`。脚本同时生成所有平台导出，避免只更新某一个图标。

本机小尺寸检查图位于 `artifacts/screens/brand-icon-sizes.png`，上半为浅底、下半为深底，每列包括真实尺寸及三倍最近邻放大。macOS 构建继续使用 `tools/mac/make-icns.sh`，正确生成 @2x 文件，最大尺寸为 1024px。
