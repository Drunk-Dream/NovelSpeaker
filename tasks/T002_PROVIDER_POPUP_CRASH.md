# T002：修复 Provider Popup 崩溃并建立核心回归

## 已确认问题

播放页展开 Provider 切换 Popup 时，Provider ItemTemplate 引用了不存在的 `App.Button.Transparent`，运行日志已确认触发 `XamlParseException`；随后 `Popup.OnWindowResize` 的 `NullReferenceException` 是前序 XAML 构造失败后的连带异常。

现有 WPF 测试没有发现问题，因为测试上下文仍保留旧 `Rules` 集合，而生产 XAML 已绑定 `Providers`，导致展开 Popup 时没有 Provider ItemTemplate 被实例化。

## 权威参考

- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T001 完成后的 Button Style 语义

## 必须完成

1. 使用 T001 确定的最终 interaction-host Button Style 修复 Provider ItemTemplate；不得通过新增 `App.Button.Transparent` 规避根因。
2. 保持整行可点击、CurrentProvider 由单一 Selection/CurrentItem Surface 表达，不重新引入双层 Hover/Selected 背景。
3. 修正 Player WPF 测试支持代码中的旧 `Rules`/Provider 数据模型偏差，使测试上下文与生产 Binding 一致。
4. 新增或重写一个永久核心回归：准备至少一个最小 Provider item，真实展开 `ProviderMenuPopup`，让 DataTemplate 完成必要的 layout/render，并验证该入口不会因 Binding/StaticResource 解析异常而失败。
5. 测试只保护“Provider 切换入口可安全打开”的核心能力，不断言具体 Style Key、Visual Tree 层级、像素、圆角或颜色。
6. 在根因修复后重新验证 `Popup.OnWindowResize` 连带异常是否消失；只有仍可独立复现时才继续调查，不为 WPF Popup 添加无证据 workaround。
7. 搜索本轮涉及的 XAML 调用点，确认没有其它对已删除/不存在 Button Style 的引用。

## 自动验收

- 旧实现下能够复现/证明核心测试覆盖到真实 Provider DataTemplate；修复后测试通过。
- Player Provider Popup focused WPF tests 通过。
- `dotnet restore --locked-mode -r win-x64`
- `dotnet format --verify-no-changes --no-restore`
- `dotnet build -c Release --no-restore`
- `dotnet test -c Release --no-build`
- Release build 0 warning / 0 error；不使用可见 Desktop 绕过隔离测试。
