using System.Windows.Threading;
using MailHelper.ViewModels;

namespace MailHelper.App;

/// <summary>IMainThreadDispatcher WPF 实现（docs/10 §4 P-10；MS2）：包装 UI 线程 Dispatcher。
/// CreateForCurrentThread 须在 UI 线程调用（等价迁移前 Dispatcher.CurrentDispatcher 在
/// MainViewModel 构造器中的语义）。Avalonia 版以 Avalonia.Threading.Dispatcher 同构实现。</summary>
public sealed class WpfMainThreadDispatcher : IMainThreadDispatcher
{
    private readonly Dispatcher _dispatcher;

    private WpfMainThreadDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public static WpfMainThreadDispatcher CreateForCurrentThread() => new(Dispatcher.CurrentDispatcher);

    public Task InvokeOnMainThreadAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public void Post(Action action) => _ = _dispatcher.BeginInvoke(action);
}
