using System.Windows;
using System.Windows.Threading;

namespace ConsecutiveSnap.App;

/// <summary>
/// アプリのエントリポイント。多重起動を禁止し、未処理例外をログに残してから通知する。
/// </summary>
public partial class App : Application
{
    /// <summary>メインウィンドウのタイトル。多重起動時に既存ウィンドウを探すためにも使う。</summary>
    internal const string MainWindowTitle = "ConsecutiveSnap";

    private Mutex? _singleInstanceMutex;

    /// <summary>起動時処理。2つ目以降のインスタンスは、既存のウィンドウを前面に出して終了する。</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\ConsecutiveSnap", out bool createdNew);
        if (!createdNew)
        {
            BringExistingWindowToFront();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        new MainWindow().Show();
    }

    /// <summary>終了時処理。多重起動防止のMutexを解放する。</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Mutexを所有していない（2つ目のインスタンス）場合は解放不要
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>既に起動中のウィンドウを探して前面に出す。見つからなければ何もしない。</summary>
    private static void BringExistingWindowToFront()
    {
        IntPtr hwnd = NativeMethods.FindWindow(null, MainWindowTitle);
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    /// <summary>UIスレッドの未処理例外。ログに残してから利用者へ通知し、アプリは継続する。</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MainWindow? main = Current.MainWindow as MainWindow;
        main?.Log.Write($"未処理例外: {e.Exception}");
        MessageBox.Show($"予期しないエラーが発生しました。\n{e.Exception.Message}",
            MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>UIスレッド以外の未処理例外。ログに残す（この後プロセスは終了する）。</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        (Current?.MainWindow as MainWindow)?.Log.Write($"致命的な例外: {e.ExceptionObject}");
    }
}
