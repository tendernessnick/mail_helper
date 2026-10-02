namespace MailHelper.ViewModels;

/// <summary>主线程调度器抽象（docs/10 §4 P-10；MS2）：解除共享 ViewModel 对具体 UI 框架的依赖。
/// WPF=System.Windows.Threading.Dispatcher；Avalonia=Avalonia.Threading.Dispatcher。实现须在主线程构造
/// （等价迁移前 Dispatcher.CurrentDispatcher 在 MainViewModel 构造器中的语义）。</summary>
public interface IMainThreadDispatcher
{
    /// <summary>切到主线程执行并等待完成（等价 WPF Dispatcher.InvokeAsync(Action) 的 await 语义）。</summary>
    Task InvokeOnMainThreadAsync(Action action);

    /// <summary>向主线程排队执行、不等待（等价 BeginInvoke；用于事件回调回 UI）。</summary>
    void Post(Action action);
}
