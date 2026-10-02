using Avalonia.Threading;
using MailHelper.ViewModels;

namespace MailHelper.App.Avalonia;

/// <summary>IMainThreadDispatcher Avalonia 实现（docs/10 §4 P-10；MS3）：包装 UI 线程 Dispatcher。
/// 与 WPF 版 WpfMainThreadDispatcher 同构（在 UI 线程构造/使用）。</summary>
public sealed class AvaloniaMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeOnMainThreadAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
